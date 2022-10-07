using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApplication1
{
    public partial class FormErrorLog : Form
    {
        public FormErrorLog()
        {
            InitializeComponent();
        }

        public bool bLogFormIsOpen;
        public string strTextToShow
        {
            get { return textBoxErrorLog.Text; }
            set
            {
                textBoxErrorLog.Text = value;
                textBoxErrorLog.SelectionStart = textBoxErrorLog.Text.Length;
                textBoxErrorLog.SelectionLength = 0;
            }
        }

        private void FormErrorLog_FormClosing(object sender, FormClosingEventArgs e)
        {
            bLogFormIsOpen = false;
        }

        public void ScrollErrorLogToBottom()
        {
            textBoxErrorLog.ScrollToCaret();
        }
    }
}
