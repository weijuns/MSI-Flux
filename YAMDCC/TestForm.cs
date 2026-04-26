using System;
using System.Windows.Forms;

namespace YAMDCC.GUI
{
    public class TestForm : Form
    {
        public TestForm()
        {
            Text = "Test Form";
            Width = 400;
            Height = 300;
            
            Label label = new Label();
            label.Text = "Hello, YAMDCC!";
            label.AutoSize = true;
            label.Location = new System.Drawing.Point(50, 50);
            Controls.Add(label);
        }
    }
}
