using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;
using BcCertificateRequest = Org.BouncyCastle.Tls.CertificateRequest;

namespace Communication.Network.RUDP;

/// <summary>
/// DTLS 1.2 핸드셰이크 실행기 — 연결 확립된 채널 위에서 <c>DtlsClientProtocol</c>/<c>DtlsServerProtocol</c>을
/// 돌리고 성공 시 <c>DtlsTransport</c>(레코드 암호화 송수신)를 돌려준다.
/// 상한 초과는 전송 폐쇄로 진행 중 핸드셰이크를 즉시 중단한다(TCP <c>TlsHandshake.AwaitAsync</c>와 같은 방식).
/// 폴링 스레드가 아니라 전용 태스크에서 호출된다.
/// </summary>
internal static class RudpDtlsHandshake
{
    internal static async Task<DtlsTransport> RunAsync(RudpDtlsTransport transport, RudpTlsOptions options, bool isServer)
    {
        Task<DtlsTransport> handshake = Task.Run(() => isServer
            ? new DtlsServerProtocol().Accept(new RudpDtlsServer(options), transport)
            : new DtlsClientProtocol().Connect(new RudpDtlsClient(options), transport));

        Task timeout = Task.Delay(options.HandshakeTimeout);
        if (await Task.WhenAny(handshake, timeout).ConfigureAwait(false) == timeout)
        {
            transport.Close(); // 대기 중 Receive를 즉시 실패시켜 핸드셰이크를 중단한다.
            _ = handshake.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default); // 중단된 핸드셰이크의 예외를 관찰 — 미관찰 예외 방지.
            throw new TimeoutException($"RUDP TLS 핸드셰이크가 {options.HandshakeTimeout}ms 안에 완료되지 않았습니다.");
        }

        return await handshake.ConfigureAwait(false); // 실패(핀닝 거절·프로토콜 오류 등) 예외 전파
    }

    /// <summary>서버 peer — 인증서 키 종류(RSA/ECDSA)에 맞는 ECDHE + AES-GCM 스위트만 제공한다.</summary>
    private sealed class RudpDtlsServer : DefaultTlsServer
    {
        private readonly byte[] _certificateDer;
        private readonly AsymmetricKeyParameter _privateKey;
        private readonly bool _isRsa;

        internal RudpDtlsServer(RudpTlsOptions options)
            : base(new BcTlsCrypto(new SecureRandom()))
        {
            X509Certificate2? certificate = options.ServerCertificate;
            if (certificate is null)
            {
                throw new InvalidOperationException("서버는 RudpTlsOptions.ServerCertificate(개인 키 포함 X509Certificate2)가 필요합니다.");
            }

            _certificateDer = certificate.GetRawCertData();

            using RSA? rsa = certificate.GetRSAPrivateKey();
            if (rsa is not null)
            {
                _isRsa = true;
                _privateKey = PrivateKeyFactory.CreateKey(rsa.ExportPkcs8PrivateKey());
                return;
            }

            using ECDsa? ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa is not null)
            {
                _isRsa = false;
                _privateKey = PrivateKeyFactory.CreateKey(ecdsa.ExportPkcs8PrivateKey());
                return;
            }

            throw new NotSupportedException("서버 인증서 개인 키는 RSA 또는 ECDSA여야 합니다.");
        }

        protected override int[] GetSupportedCipherSuites() => _isRsa
            ? new[] { CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256, CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384 }
            : new[] { CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256, CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384 };

        protected override ProtocolVersion[] GetSupportedVersions()
            => new[] { ProtocolVersion.DTLSv12 }; // 미지정 시 서버가 협상에 쓸 버전이 없다(protocol_version 알림으로 실패)

        protected override TlsCredentialedSigner GetRsaSignerCredentials() => CreateSigner(isRsa: true);

        protected override TlsCredentialedSigner GetECDsaSignerCredentials() => CreateSigner(isRsa: false);

        private TlsCredentialedSigner CreateSigner(bool isRsa)
        {
            if (_isRsa != isRsa)
            {
                // 인증서 키가 아닌 쪽 스위트는 GetSupportedCipherSuites에서도 제외했다 — 여기로 오는 일은 없다.
                return null!;
            }

            BcTlsCertificate leaf = new((BcTlsCrypto)Crypto, _certificateDer);
            Certificate chain = new(new Org.BouncyCastle.Tls.Crypto.TlsCertificate[] { leaf });
            return new BcDefaultTlsCredentialedSigner(
                new Org.BouncyCastle.Tls.Crypto.TlsCryptoParameters(m_context), // 'Crypto'는 상속 속성과 이름이 겹친다 — 완전 한정
                (BcTlsCrypto)Crypto,
                _privateKey,
                chain,
                new SignatureAndHashAlgorithm(
                    Org.BouncyCastle.Tls.HashAlgorithm.sha256,
                    isRsa ? Org.BouncyCastle.Tls.SignatureAlgorithm.rsa : Org.BouncyCastle.Tls.SignatureAlgorithm.ecdsa));
        }
    }

    /// <summary>클라이언트 peer — DTLS 1.2 고정, 서버 인증서 검증은 <see cref="RudpDtlsAuthentication"/>으로 위임.</summary>
    private sealed class RudpDtlsClient : DefaultTlsClient
    {
        private readonly RudpTlsOptions _options;

        internal RudpDtlsClient(RudpTlsOptions options)
            : base(new BcTlsCrypto(new SecureRandom()))
        {
            _options = options;
        }

        protected override ProtocolVersion[] GetSupportedVersions()
            => new[] { ProtocolVersion.DTLSv12 };

        protected override int[] GetSupportedCipherSuites() => new[]
        {
            CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
            CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
            CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
            CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        };

        public override TlsAuthentication GetAuthentication() => new RudpDtlsAuthentication(_options);
    }

    /// <summary>
    /// 클라이언트 인증 — 핀닝 콜백 → TargetHost 이름 일치 → 둘 다 없으면 기본 거부(fail-closed).
    /// OS 인증서 스토어 검증 계약이 없는 DTLS에서 무검증 수용은 중간자 공격을 연다.
    /// </summary>
    private sealed class RudpDtlsAuthentication : TlsAuthentication
    {
        private readonly RudpTlsOptions _options;

        internal RudpDtlsAuthentication(RudpTlsOptions options)
        {
            _options = options;
        }

        public TlsCredentials GetClientCredentials(BcCertificateRequest certificateRequest)
            => null!; // 클라이언트 인증서 미지원(v1) — 서버 인증만 한다.

        public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
        {
            Certificate certificate = serverCertificate.Certificate;
            if (certificate.IsEmpty)
            {
                throw new TlsFatalAlert(AlertDescription.certificate_unknown, "서버가 인증서를 보내지 않았습니다.");
            }

            byte[] der = certificate.GetCertificateAt(0).GetEncoded();

            if (_options.RemoteCertificateValidation is { } validate)
            {
                if (!validate(der))
                {
                    throw new TlsFatalAlert(AlertDescription.certificate_unknown, "서버 인증서가 검증 콜백에서 거부됐습니다(핀닝 불일치 등).");
                }

                return;
            }

            if (_options.TargetHost is { } targetHost)
            {
                if (!MatchesHost(der, targetHost))
                {
                    throw new TlsFatalAlert(AlertDescription.bad_certificate, $"인증서 이름이 대상 호스트 '{targetHost}'와 일치하지 않습니다.");
                }

                return;
            }

            throw new TlsFatalAlert(
                AlertDescription.certificate_unknown,
                "서버 인증서 검증 수단이 없습니다 — RudpTlsOptions.RemoteCertificateValidation(핀닝) 또는 TargetHost를 설정하십시오.");
        }

        /// <summary>SAN dNSName(우선)과 CN으로 대상 호스트명 일치 검사 — 대소문자 무시.</summary>
        private static bool MatchesHost(byte[] certificateDer, string targetHost)
        {
            BcX509Certificate certificate = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(certificateDer);

            System.Collections.Generic.IList<System.Collections.Generic.IList<object>>? subjectAlternativeNames = certificate.GetSubjectAlternativeNames();
            if (subjectAlternativeNames is not null)
            {
                foreach (System.Collections.Generic.IList<object> san in subjectAlternativeNames)
                {
                    if (san.Count >= 2
                        && Convert.ToInt32(san[0]) == 2 // dNSName
                        && string.Equals(san[1] as string, targetHost, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            foreach (string commonName in certificate.SubjectDN.GetValueList(X509Name.CN))
            {
                if (string.Equals(commonName, targetHost, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
