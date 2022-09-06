using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Buffers.Binary;


namespace DVDUtilities
{
    #region NEW_VMG
    public class NEW_VMG
    {
        public string? Identifier { get; private set; }
        public System.UInt32 LastSectorVMG { get; private set; }
        public System.UInt32 LastSectorIFO { get; private set; }
        public System.Decimal Version { get; private set; }
        public System.UInt32 VMGCategory { get; private set; }
        public System.UInt16 NumberOfVolumes { get; private set; }
        public System.UInt16 VolumeNumber { get; private set; }
        public System.Byte SideID { get; private set; }
        public System.UInt16 NumberTitleSets { get; private set; }
        public string? ProviderID { get; private set; }
        public System.UInt64 VmgPos { get; private set; }
        public System.UInt32 EndByteAddress_VMGI_MAT { get; private set; }
        public System.UInt32 StartAddress_FP_PGC { get; private set; }
        public System.UInt32 StartSector_Menu_VOB { get; private set; }
        public System.UInt32 SectorPointer_TT_SRPT { get; private set; }
        public System.UInt32 SectorPointer_VMGM_PGCI_UT { get; private set; }
        public System.UInt32 SectorPointer_VMG_PTL_MAIT { get; private set; }
        public System.UInt32 SectorPointer_VMG_VTS_ATRT { get; private set; }
        public System.UInt32 SectorPointer_VMG_TXTDT_MG { get; private set; }
        public System.UInt32 SectorPointer_VMGM_C_ADT { get; private set; }
        public System.UInt32 SectorPointer_VMGM_VOBU_ADMAP { get; private set; }
        public System.UInt16 VideoAttributes_VMGM_VOBS { get; private set; }
        public System.UInt16 NumberAudioStreams_VMGM_VOBS { get; private set; }
        public NewAudioAttributes[]? AudioAttributes_VMGM_VOBS { get; private set; }
        public System.UInt16 NumberSubpictureStreams_VMGM_VOBS { get; private set; }
        public NewSubpictureAttributes? SubpictureAttributes_VMGM_VOBS { get; private set; }


        // In case the user already created the BinaryReader
        public NEW_VMG(BinaryReader bin)
        {
            Init(bin);
        }

        // In case the user sends a filename
        public NEW_VMG(string _path)
        {
            byte[] bytes = File.ReadAllBytes(_path);
            using (var stream = new MemoryStream(bytes, 0, bytes.Length, false))
            {
                using (var bin = new BinaryReader(stream))
                {
                    Init(bin);
                }
            }
        }


        //
        // DVD structures are big endian, Windows is little endian.  Get32 swaps the bytes

        private void Init(BinaryReader bin)
        {
//            try
            {
                Identifier = new string(bin.ReadChars(12));
                LastSectorVMG = bin.ReadUInt32();
                bin.BaseStream.Position = 0x1C;
                LastSectorIFO = bin.ReadUInt32();


                var t = bin.ReadUInt16() >> 8;
                string major = (t >> 4 & 15).ToString();
                string minor = (t & 15).ToString();
                Version = Decimal.Parse( major + "." + minor);

                VMGCategory = reverse(bin.ReadUInt32());
                NumberOfVolumes = reverse(bin.ReadUInt16());
                VolumeNumber = reverse(bin.ReadUInt16());
                SideID = bin.ReadByte();
                bin.BaseStream.Position = 0x3E;
                NumberTitleSets = reverse(bin.ReadUInt16());
                ProviderID = new string(bin.ReadChars(32));
                VmgPos = reverse(bin.ReadUInt64());
                bin.BaseStream.Position = 0x80;
                EndByteAddress_VMGI_MAT = reverse(bin.ReadUInt32());
                StartAddress_FP_PGC = reverse(bin.ReadUInt32());
                bin.BaseStream.Position = 0xC0;
                StartSector_Menu_VOB = reverse(bin.ReadUInt32());
                SectorPointer_TT_SRPT = reverse(bin.ReadUInt32());
                SectorPointer_VMGM_PGCI_UT = reverse(bin.ReadUInt32());
                SectorPointer_VMG_PTL_MAIT = reverse(bin.ReadUInt32());
                bin.BaseStream.Position = 0xD0;
                SectorPointer_VMG_VTS_ATRT = reverse(bin.ReadUInt32());
                SectorPointer_VMG_TXTDT_MG = reverse(bin.ReadUInt32());
                SectorPointer_VMGM_C_ADT = reverse(bin.ReadUInt32());
                SectorPointer_VMGM_VOBU_ADMAP = reverse(bin.ReadUInt32());
                bin.BaseStream.Position = 0x100;
                VideoAttributes_VMGM_VOBS = reverse(bin.ReadUInt16());
                NumberAudioStreams_VMGM_VOBS = reverse(bin.ReadUInt16());
                AudioAttributes_VMGM_VOBS = new NewAudioAttributes[8];
                for (int i = 0; i < 8; ++i)
                    AudioAttributes_VMGM_VOBS[i] = new NewAudioAttributes(bin.ReadBytes(8));
                bin.BaseStream.Position = 0x154;
                NumberSubpictureStreams_VMGM_VOBS = reverse(bin.ReadUInt16());
                SubpictureAttributes_VMGM_VOBS = new NewSubpictureAttributes(bin.ReadBytes(6));
            }
//            catch { }  // Don't do anything, but don't set values
        }

        private System.UInt16 reverse(System.UInt16 n) { return BinaryPrimitives.ReverseEndianness(n); }
        private System.UInt32 reverse(System.UInt32 n) { return BinaryPrimitives.ReverseEndianness(n); }
        private System.UInt64 reverse(System.UInt64 n) { return BinaryPrimitives.ReverseEndianness(n); }

        ~NEW_VMG()
        {
            // Explicitly tell the GC we are done with this
            if (Identifier != null) Identifier = null;
            if (ProviderID != null) ProviderID = null;
            if (AudioAttributes_VMGM_VOBS != null) AudioAttributes_VMGM_VOBS = null;
            if (SubpictureAttributes_VMGM_VOBS != null) SubpictureAttributes_VMGM_VOBS = null;
        }

    }
    #endregion

    #region NewAudioAttributes
    public class NewAudioAttributes
    {
        public byte[]? _data;

        public NewAudioAttributes()
        {
            if (_data == null)
                _data = new byte[8];
        }

        public NewAudioAttributes(byte[]? data)
        {
            _data = data;
        }   

        ~NewAudioAttributes()
        {
            // Explicitly tell the GC we are done with this
            if (_data != null)
                _data = null;
        }

    }
    #endregion

    #region NewSubpictureAttributes
    public class NewSubpictureAttributes
    {
        public byte[]? _data;

        public NewSubpictureAttributes()
        {
            if (_data == null)
                _data = new byte[6];
        }

        public NewSubpictureAttributes(byte[]? data)
        {
            _data = data;
        }

        ~NewSubpictureAttributes()
        {
            // Explicitly tell the GC we are done with this
            if (_data != null)
                _data = null;
        }


    }
    #endregion
}