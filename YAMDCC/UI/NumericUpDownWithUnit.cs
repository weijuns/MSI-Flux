using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Globalization;
using System.Text.RegularExpressions;

namespace YAMDCC.GUI.UI
{
    public class NumericUpDownWithUnit : NumericUpDown
    {
        #region| Fields |

        private string unit = null;
        private bool unitFirst = false;

        #endregion

        #region| Properties |

        public string Unit
        {
            get => unit;
            set
            {
                unit = value;

                UpdateEditText();
            }
        }

        public bool UnitFirst
        {
            get => unitFirst;
            set
            {
                unitFirst = value;

                UpdateEditText();
            }
        }

        #endregion

        #region| Methods |

        protected override void UpdateEditText()
        {
            if (Unit != null && Unit != string.Empty)
            {
                if (UnitFirst)
                {
                    Text = $"({Unit}) {Value}";
                }
                else
                {
                    Text = $"{Value} ({Unit})";
                }
            }
            else
            {
                base.UpdateEditText();
            }
        }

        protected override void ValidateEditText()
        {
            ParseEditText();
            UpdateEditText();
        }

        protected new void ParseEditText()
        {
            try
            {
                var regex = new Regex($@"[^(?!{Unit} )]+");
                var match = regex.Match(Text);

                if (match.Success)
                {
                    var text = match.Value;

                    if (!string.IsNullOrEmpty(text) && !(text.Length == 1 && text == "-"))
                    {
                        if (Hexadecimal)
                        {
                            Value = Constrain(Convert.ToDecimal(Convert.ToInt32(Text, 16)));
                        }
                        else
                        {
                            Value = Constrain(Decimal.Parse(text, CultureInfo.CurrentCulture));
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                UserEdit = false;
            }
        }

        private decimal Constrain(decimal value)
        {
            if (value < Minimum)
            {
                value = Minimum;
            }

            if (value > Maximum)
            {
                value = Maximum;
            }

            return value;
        }

        #endregion
    }
}
