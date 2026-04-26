using System;
using System.Windows.Forms;

namespace MSIFlux.GUI
{
    public class SimpleTestForm : Form
    {
        public SimpleTestForm()
        {
            this.Text = "Simple Test";
            this.Size = new System.Drawing.Size(400, 300);
            this.StartPosition = FormStartPosition.CenterScreen;
            
            Label label = new Label();
            label.Text = "Hello, MSIFlux!";
            label.Location = new System.Drawing.Point(50, 50);
            label.Size = new System.Drawing.Size(200, 30);
            this.Controls.Add(label);
        }
    }
}
