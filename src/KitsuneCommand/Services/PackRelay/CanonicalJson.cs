// Canonical JSON encoder — must produce byte-identical output to
// PackRelayCloud's src/lib/canonical-json.ts. That cloud-side encoder
// is what generates the bytes the publisher's Ed25519 key signs;
// re-creating those exact bytes here is the only way KC's signature
// will verify on the cloud.
//
// Rules (per packrelay.cloud /docs/spec § canonicalization):
//   - UTF-8 output
//   - Object keys sorted lexicographically (US-ASCII codepoint order;
//     StringComparer.Ordinal in .NET, Object.keys(obj).sort() in JS)
//   - No whitespace between tokens
//   - JSON natives only: string, number, boolean, null, array, object
//
// String escapes mirror JavaScript's JSON.stringify default behavior:
//   - " -> \", \ -> \\
//   - Control chars 0x00-0x1F via \b \f \n \r \t or \uXXXX
//   - Forward slashes NOT escaped (matches JSON.stringify default)
//   - Non-ASCII codepoints emitted literally; UTF-8 encoding happens
//     at the final GetBytes step
//
// Numbers:
//   - Integer-valued doubles print without decimal (45105 not 45105.0)
//     to match JS's JSON.stringify(45105.0) === "45105"
//   - Non-integers use "R" round-trip formatter
//   - Manifest content is integer-only in practice (sizes,
//     schemaVersion); the hot path stays simple
//
// This port takes Newtonsoft's JToken as input because KC already
// uses Newtonsoft heavily — introducing a foreign JsonTree type
// (like the PackRelayServerTools mod did) would add noise without
// upside. JToken's JTokenType maps cleanly to the cases we care
// about; we explicitly reject types JSON-spec doesn't model (Date,
// Bytes, Guid, etc.) rather than coerce silently.
//
// NOT a full RFC 8785 implementation. Numeric edge cases (Infinity,
// -0, > 2^53) match JS-equivalent behavior only; we don't claim
// correctness outside that envelope.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace KitsuneCommand.Services.PackRelay
{
    public static class CanonicalJson
    {
        /// <summary>
        /// Canonicalize a JToken tree into UTF-8 bytes ready for hashing
        /// or signing. The output is byte-identical to what
        /// PackRelayCloud's <c>canonicalize()</c> + TextEncoder
        /// produces for the same logical value.
        /// </summary>
        public static byte[] Encode(JToken value)
        {
            return Encoding.UTF8.GetBytes(EncodeToString(value));
        }

        /// <summary>
        /// Canonicalize to a string. Useful for tests + log inspection;
        /// production callers should prefer <see cref="Encode"/> so they
        /// hash/sign over UTF-8 bytes directly.
        /// </summary>
        public static string EncodeToString(JToken value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, JToken v)
        {
            if (v == null || v.Type == JTokenType.Null)
            {
                sb.Append("null");
                return;
            }

            switch (v.Type)
            {
                case JTokenType.Boolean:
                    sb.Append(v.Value<bool>() ? "true" : "false");
                    return;

                case JTokenType.Integer:
                    // JToken.Integer is stored as long internally for
                    // values that fit; for bigger integers Newtonsoft
                    // promotes to BigInteger. Stick to long here —
                    // manifests don't go past 2^63 in practice (largest
                    // realistic file size is ~10TB << 2^53).
                    sb.Append(v.Value<long>().ToString(CultureInfo.InvariantCulture));
                    return;

                case JTokenType.Float:
                    WriteFloat(sb, v.Value<double>());
                    return;

                case JTokenType.String:
                    // Newtonsoft may also represent URIs and Guids as
                    // String at the JToken layer; Value<string>() pulls
                    // the underlying text in all those cases.
                    WriteString(sb, v.Value<string>() ?? "");
                    return;

                case JTokenType.Array:
                    WriteArray(sb, (JArray)v);
                    return;

                case JTokenType.Object:
                    WriteObject(sb, (JObject)v);
                    return;

                // Types that don't round-trip through standard JSON
                // are a programmer error here. Dates, byte arrays,
                // Guids etc. should be serialized to strings by the
                // caller before they reach CanonicalJson — otherwise
                // the cloud's TS-side canonicalization would diverge
                // (Date.toISOString() vs C#'s "o" vs "u" differ).
                case JTokenType.Date:
                case JTokenType.Bytes:
                case JTokenType.Guid:
                case JTokenType.TimeSpan:
                case JTokenType.Uri:
                    throw new InvalidOperationException(
                        "Canonical JSON: " + v.Type +
                        " is not a JSON-native type. Serialize to a string before canonicalizing.");

                default:
                    throw new InvalidOperationException(
                        "Canonical JSON: unsupported JTokenType " + v.Type);
            }
        }

        private static void WriteFloat(StringBuilder sb, double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n))
            {
                throw new InvalidOperationException(
                    "Canonical JSON: non-finite number");
            }
            // Integer-valued doubles emit without decimal so the output
            // matches JavaScript's JSON.stringify (which prints 45105
            // not 45105.0). For values outside the long-safe range,
            // fall through to "R" round-trip formatting.
            if (n == Math.Floor(n) && Math.Abs(n) < 1e15)
            {
                sb.Append(((long)n).ToString(CultureInfo.InvariantCulture));
                return;
            }
            sb.Append(n.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\b') sb.Append("\\b");
                else if (c == '\f') sb.Append("\\f");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20)
                {
                    // Other control chars -> \u00XX. Matches what
                    // JSON.stringify emits for these.
                    sb.Append("\\u");
                    sb.Append(((int)c).ToString("x4",
                        CultureInfo.InvariantCulture));
                }
                else
                {
                    // Everything else (including non-ASCII) goes
                    // through literally. UTF-8 encoding happens when
                    // we go from string -> bytes.
                    sb.Append(c);
                }
            }
            sb.Append('"');
        }

        private static void WriteArray(StringBuilder sb, JArray arr)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in arr)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
        }

        private static void WriteObject(StringBuilder sb, JObject obj)
        {
            // Keys sorted lexicographically by US-ASCII codepoint
            // order — must match Object.keys(obj).sort() in JS.
            // StringComparer.Ordinal is the .NET equivalent (compares
            // raw codepoint values, no culture/collation).
            var keys = obj.Properties()
                .Select(p => p.Name)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            sb.Append('{');
            bool first = true;
            foreach (var key in keys)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, key);
                sb.Append(':');
                WriteValue(sb, obj[key]);
            }
            sb.Append('}');
        }
    }
}
