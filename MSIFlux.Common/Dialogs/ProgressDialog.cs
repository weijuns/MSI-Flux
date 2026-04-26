// This file is part of MSIFlux (Yet Another MSI Dragon Center Clone).
// Copyright © MSIFlux_Config and Contributors 2023-2026.
//
// MSIFlux is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// MSIFlux is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
// more details.
//
// You should have received a copy of the GNU General Public License along with
// MSIFlux. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MSIFlux.Common.Dialogs;

public sealed partial class ProgressDialog<TResult> : Form
{
    #region Disable close button
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cParams = base.CreateParams;
            cParams.ClassStyle |= 0x200;   // CP_NOCLOSE_BUTTON
            return cParams;
        }
    }
    #endregion

    private readonly System.Windows.Forms.Timer DisplayTimer = new();

    public TResult Result { get; set; }

    public Func<TResult> DoWork { get; set; }

    public string Caption
    {
        get => lblCaption.Text;
        set => lblCaption.Text = string.IsNullOrEmpty(value)
            ? "Please wait..."
            : value;
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="ProgressDialog{TResult}"/> class.
    /// </summary>
    public ProgressDialog()
    {
        Opacity = 0;
        InitializeComponent();

        pbProgress.Style = ProgressBarStyle.Marquee;

        DisplayTimer.Interval = 1000;
        DisplayTimer.Tick += new EventHandler(ShowProgress);
    }

    private async void OnLoad(object sender, EventArgs e)
    {
        // sanity check
        if (DoWork is null)
        {
            throw new InvalidOperationException("The DoWork property is null.");
        }
        DisplayTimer.Start();
        Result = await Task.Run(DoWork);
        Close();
    }

    private void ShowProgress(object sender, EventArgs e)
    {
        Opacity = 1;
        DisplayTimer.Stop();
    }
}
