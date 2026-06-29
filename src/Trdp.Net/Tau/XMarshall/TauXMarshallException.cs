// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tau_xmarshall.c
// (Fehlerrueckgaben TRDP_ERR_T der tau_x*-Funktionen).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using Trdp.Net.Vos;

namespace Trdp.Net.Tau.XMarshall
{
    /// <summary>
    /// DE: Ausnahme der XMarshall-Schicht. Traegt den original-aequivalenten
    /// <see cref="TrdpError"/>-Code (z. B. <see cref="TrdpError.ComIdErr"/>,
    /// <see cref="TrdpError.InitErr"/>), damit Aufrufer auf die gleichen Faelle
    /// reagieren koennen wie die C-Funktionen ueber ihren Rueckgabewert.
    /// </summary>
    public sealed class TauXMarshallException : Exception
    {
        /// <summary>DE: Original-aequivalenter TRDP-Fehlercode.</summary>
        public TrdpError Error { get; }

        public TauXMarshallException(TrdpError error, string message) : base(message)
        {
            Error = error;
        }
    }
}
