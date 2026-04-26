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

namespace MSIFlux.Common.Configs;

/// <summary>
/// The exception thrown when an invalid <see cref="MSIFlux_Config"/> is loaded.
/// </summary>
public sealed class InvalidConfigException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidConfigException"/> class.
    /// </summary>
    public InvalidConfigException()
        : base("The config was not in the expected format.") { }
}
