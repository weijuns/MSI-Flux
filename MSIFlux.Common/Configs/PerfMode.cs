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
/// Represents a configuration for an
/// individual performance mode of a laptop.
/// </summary>
public sealed class PerfMode
{
    /// <summary>
    /// The name of the performance mode.
    /// </summary>
    [XmlElement]
    public string Name { get; set; }

    /// <summary>
    /// The description of the performance mode.
    /// </summary>
    [XmlElement]
    public string Desc { get; set; }

    /// <summary>
    /// The value to write to the EC register
    /// when this performance mode is selected.
    /// </summary>
    [XmlElement]
    public byte Value { get; set; }
}
