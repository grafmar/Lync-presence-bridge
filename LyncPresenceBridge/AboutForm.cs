using System;
using System.Windows.Forms;

namespace LyncPresenceBridge
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
        }

        private void buttonAboutOK_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/grafmar/Lync-presence-bridge");
        }

		private void AboutForm_Load(object sender, EventArgs e)
		{
			label2.Text = "This is version: V" + Application.ProductVersion;
		}

		private void label1_Click(object sender, EventArgs e)
		{
		}

		private void label2_Click(object sender, EventArgs e)
		{

		}
	}
}
