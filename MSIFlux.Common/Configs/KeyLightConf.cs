// This file is part of MSIFlux, based on YAMDCC.
// Original Copyright © 2023-2025 Sparronator9999
// Modifications Copyright © 2026 weijuns.
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
// more details.
//
// You should have received a copy of the GNU General Public License along with
// This program. If not, see <https://www.gnu.org/licenses/>.

using System.Xml.Serialization;

namespace MSIFlux.Common.Configs;

/// <summary>
/// Represents a configuration for the keyboard backlight in a laptop.
/// </summary>
public sealed class KeyLightConf
{
    /// <summary>
    /// The register that controls the keyboard backlight.
    /// </summary>
    [XmlElement]
    public byte Reg { get; set; }

    /// <summary>
    /// The value that turns off the backlight
    /// (or reduces it to its minimum brightness).
    /// </summary>
    [XmlElement]
    public byte MinVal { get; set; }

    /// <summary>
    /// The value that sets the keyboard
    /// backlight to the maximum brightness.
    /// </summary>
    [XmlElement]
    public byte MaxVal { get; set; }
}
