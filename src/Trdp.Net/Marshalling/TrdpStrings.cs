// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Komfort-Helfer fuer CHAR8-/UTF16-Felder in TRDP-Datasets (im C-Original implizit ueber
// CHAR8-/UTF16-Arrays). Gepackt, big-endian, nullterminiert/-gefuellt.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Text;

namespace Trdp.Net.Marshalling
{
    /// <summary>
    /// DE: Lesen/Schreiben von String-Feldern (CHAR8 = UTF-8-faehig, UTF16 = Big-Endian-Unicode)
    /// fester Laenge im Wire-Format. Ergaenzt <see cref="TrdpWireWriter"/>/<see cref="TrdpWireReader"/>.
    /// </summary>
    public static class TrdpStrings
    {
        /// <summary>DE: Schreibt einen String als CHAR8[<paramref name="count"/>] (UTF-8, nullgefuellt/-abgeschnitten).</summary>
        public static void PutChar8Fixed(ref TrdpWireWriter w, string value, int count)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            for (int i = 0; i < count; i++)
            {
                w.PutUInt8(i < bytes.Length ? bytes[i] : (byte)0);
            }
        }

        /// <summary>DE: Liest CHAR8[<paramref name="count"/>] als String (bis zur ersten Null bzw. count).</summary>
        public static string GetChar8Fixed(ref TrdpWireReader r, int count)
        {
            var buf = new byte[count];
            for (int i = 0; i < count; i++) buf[i] = r.GetUInt8();
            int len = Array.IndexOf(buf, (byte)0);
            if (len < 0) len = count;
            return Encoding.UTF8.GetString(buf, 0, len);
        }

        /// <summary>DE: Schreibt einen String als UTF16[<paramref name="count"/>] (Big-Endian, nullgefuellt).</summary>
        public static void PutUtf16Fixed(ref TrdpWireWriter w, string value, int count)
        {
            value ??= string.Empty;
            for (int i = 0; i < count; i++)
            {
                w.PutChar16(i < value.Length ? value[i] : '\0');
            }
        }

        /// <summary>DE: Liest UTF16[<paramref name="count"/>] als String (bis zur ersten Null bzw. count).</summary>
        public static string GetUtf16Fixed(ref TrdpWireReader r, int count)
        {
            var sb = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                char c = r.GetChar16();
                if (c == '\0') { for (int j = i + 1; j < count; j++) r.GetChar16(); break; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
