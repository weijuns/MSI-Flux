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
