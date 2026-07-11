using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientGUI
{
    public class UniqueForm : Form
    {
        static Dictionary<Type, Form> _instances = new();

        public static T FindInstance<T>() where T : Form
        {
            if (_instances.TryGetValue(typeof(T), out var form))
            {
                return form as T;
            }
            return null;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if(_instances.TryGetValue(this.GetType(), out var form))
            {
                form.Close();
                _instances.Remove(this.GetType());
            }

            _instances.Add(this.GetType(), this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            _instances.Remove(this.GetType());
        }
    }
}
