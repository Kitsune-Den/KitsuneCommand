using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace KitsuneCommand.GameIntegration.Harmony
{
    /// <summary>
    /// Patches NetPackageSetBlockTexture to widen the paint texture index from byte (0-255)
    /// to ushort (0-65535), breaking the 255-paint hard limit imposed by the vanilla network layer.
    ///
    /// Root cause:
    ///   NetPackageSetBlockTexture stores the texture index as a byte field (idx), meaning any
    ///   texture index above 255 is silently truncated during Setup(), then serialized as a single
    ///   byte over the wire. BlockTextureData.TextureID is a ushort internally, so the engine
    ///   supports up to 65535 textures - the bottleneck is purely this network packet.
    ///
    /// Fix:
    ///   - Patch Setup() postfix to capture the full int _idx before vanilla truncates to byte.
    ///   - Patch write() to serialize with magic header + ushort for indices above 254,
    ///     falling back to vanilla single-byte format for 0-254 (full backward compat).
    ///   - Patch read() to detect magic header and deserialize ushort accordingly.
    ///
    /// Compatibility:
    ///   Both server and client need this patch for indices above 255 to work.
    ///   Unpatched clients/servers work fine for indices 0-254.
    ///   Index 255 (0xFF) is the magic sentinel - don't assign a paint to slot 255
    ///   if you need mixed patched/unpatched compatibility.
    /// </summary>
    // NOTE: No [HarmonyPatch] attribute — this patch is applied manually in ModLifecycle.PatchByHarmony()
    // only when PaintUnlocked (0_PaintUnlocked) is detected. Without it, vanilla clients sending
    // paint packets would crash the server's network deserializer.
    public static class PaintIndexWidenerPatch
    {
        private const byte ExtendedIndexMagic = 0xFF;

        // Side-channel ushort storage, keyed by instance identity hash.
        private static readonly Dictionary<int, ushort> _idxMap = new Dictionary<int, ushort>();
        private static readonly object _idxLock = new object();

        // Cached field infos
        private static readonly FieldInfo _fIdx =
            typeof(NetPackageSetBlockTexture).GetField("idx", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fBlockPos =
            typeof(NetPackageSetBlockTexture).GetField("blockPos", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fBlockFace =
            typeof(NetPackageSetBlockTexture).GetField("blockFace", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fPlayerId =
            typeof(NetPackageSetBlockTexture).GetField("playerIdThatChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fChannel =
            typeof(NetPackageSetBlockTexture).GetField("channel", BindingFlags.NonPublic | BindingFlags.Instance);

        // PooledBinaryWriter.Write overloads resolved by parameter type - avoids Span ambiguity
        private static readonly MethodInfo _writeInt =
            typeof(PooledBinaryWriter).GetMethod("Write", new[] { typeof(int) });
        private static readonly MethodInfo _writeByte =
            typeof(PooledBinaryWriter).GetMethod("Write", new[] { typeof(byte) });
        private static readonly MethodInfo _writeUshort =
            typeof(PooledBinaryWriter).GetMethod("Write", new[] { typeof(ushort) });

        private static void StoreIdx(NetPackageSetBlockTexture instance, ushort value)
        {
            var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
            lock (_idxLock) { _idxMap[key] = value; }
            _fIdx?.SetValue(instance, (byte)(value & 0xFF));
        }

        private static ushort LoadIdx(NetPackageSetBlockTexture instance)
        {
            var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
            lock (_idxLock) { if (_idxMap.TryGetValue(key, out var v)) return v; }
            return _fIdx != null ? (byte)_fIdx.GetValue(instance) : (byte)0;
        }

        // -----------------------------------------------------------------------
        // Setup() postfix — capture full int before vanilla truncates to byte
        // -----------------------------------------------------------------------
        [HarmonyPatch("Setup")]
        [HarmonyPostfix]
        public static void SetupPostfix(NetPackageSetBlockTexture __instance, int _idx)
        {
            StoreIdx(__instance, (ushort)_idx);
        }

        // -----------------------------------------------------------------------
        // write() — replace vanilla serialization
        // -----------------------------------------------------------------------
        [HarmonyPatch("write")]
        [HarmonyPrefix]
        public static bool WritePrefix(NetPackageSetBlockTexture __instance, PooledBinaryWriter _bw)
        {
            var blockPos  = (Vector3i)_fBlockPos.GetValue(__instance);
            var blockFace = (BlockFace)_fBlockFace.GetValue(__instance);
            var playerId  = (int)_fPlayerId.GetValue(__instance);
            var channel   = (byte)_fChannel.GetValue(__instance);
            var idx       = LoadIdx(__instance);

            // Use reflection-resolved Write overloads to avoid Span ambiguity in net48
            _writeInt.Invoke(_bw, new object[] { blockPos.x });
            _writeInt.Invoke(_bw, new object[] { blockPos.y });
            _writeInt.Invoke(_bw, new object[] { blockPos.z });
            _writeByte.Invoke(_bw, new object[] { (byte)blockFace });
            _writeInt.Invoke(_bw, new object[] { playerId });
            _writeByte.Invoke(_bw, new object[] { channel });

            if (idx > 254)
            {
                _writeByte.Invoke(_bw, new object[] { ExtendedIndexMagic });
                _writeUshort.Invoke(_bw, new object[] { idx });
            }
            else
            {
                _writeByte.Invoke(_bw, new object[] { (byte)idx });
            }

            return false; // suppress original
        }

        // -----------------------------------------------------------------------
        // read() — detect extended format
        // -----------------------------------------------------------------------
        [HarmonyPatch("read")]
        [HarmonyPrefix]
        public static bool ReadPrefix(NetPackageSetBlockTexture __instance, PooledBinaryReader _br)
        {
            _fBlockPos.SetValue(__instance, new Vector3i(_br.ReadInt32(), _br.ReadInt32(), _br.ReadInt32()));
            _fBlockFace.SetValue(__instance, (BlockFace)_br.ReadByte());
            _fPlayerId.SetValue(__instance, _br.ReadInt32());
            _fChannel.SetValue(__instance, _br.ReadByte());

            var firstByte = _br.ReadByte();
            ushort idx = firstByte == ExtendedIndexMagic
                ? _br.ReadUInt16()
                : firstByte;

            StoreIdx(__instance, idx);

            return false; // suppress original
        }
    }
}
