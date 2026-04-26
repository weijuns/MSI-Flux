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

using System.Collections.Generic;
using System.Xml.Serialization;

namespace MSIFlux.Common.Configs;

/// <summary>
/// Represents a configuration for the performance modes of a laptop
/// (separate from the Windows power plans).
/// </summary>
public sealed class PerfModeConf
{
    /// <summary>
    /// The register that controls the performance mode.
    /// </summary>
    [XmlElement]
    public byte Reg { get; set; }

    /// <summary>
    /// The default performance mode, as an index of the available
    /// performance modes, when not overriden by a <see cref="FanCurveConf"/>.
    /// </summary>
    [XmlElement]
    public int ModeSel { get; set; }

    /// <summary>
    /// An array of possible performance modes for the laptop.
    /// </summary>
    [XmlArray]
    public List<PerfMode> PerfModes { get; set; }
}
