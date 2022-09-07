using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Buffers.Binary;


/*
 * 
 * C# marshaling of arrays requires that each entry of the array be aligned to a DWORD.
 * This means that an array of 6-byte structures breaks every time. As a result I've
 * Used the BinaryReader method of parsing the fields.
 * 
 */

namespace DVDUtilities
{
    #region VmgIfo
    public class VmgIfo
    {
        private static System.UInt16 DVD_BLOCK_LEN = 2048;
        private static System.UInt32 MAX_IFO_SIZE = 256000;
        private System.UInt16 TT_SRPT_Count;

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
        public VideoAttributes? VideoAttributes_VMGM_VOBS { get; private set; }
        public System.UInt16 NumberAudioStreams_VMGM_VOBS { get; private set; }
        public AudioAttributes[]? AudioAttributes_VMGM_VOBS { get; private set; }
        public System.UInt16 NumberSubpictureStreams_VMGM_VOBS { get; private set; }
        public SubpictureAttributes? SubpictureAttributes_VMGM_VOBS { get; private set; }
        public TT_SRPT_Type[]? TT_SRPT { get; private set; }


        // In case the user already created the BinaryReader
        public VmgIfo(BinaryReader bin)
        {
            Init(bin);
        }

        // In case the user sends a filename
        public VmgIfo(string _path)
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


        /*
         * 
         * DVD structures are big endian, so use the private reverse functions to fix.
         * The reverse() functions are kept local to each class that needs them so they
         * can be used outside of the VMG/VTS stuff if desired.
         * 
         */

        private void Init(BinaryReader bin)
        {
            Identifier = new string(bin.ReadChars(12));
            LastSectorVMG = bin.ReadUInt32();
            bin.BaseStream.Position = 0x1C;
            LastSectorIFO = bin.ReadUInt32();


            var t = bin.ReadUInt16() >> 8;
            string major = (t >> 4 & 15).ToString();
            string minor = (t & 15).ToString();
            Version = Decimal.Parse(major + "." + minor);

            VMGCategory = reverse(bin.ReadUInt32());
            NumberOfVolumes = reverse(bin.ReadUInt16());
            VolumeNumber = reverse(bin.ReadUInt16());
            SideID = bin.ReadByte();
            bin.BaseStream.Position = 0x3E;
            NumberTitleSets = reverse(bin.ReadUInt16());
            ProviderID = new string(bin.ReadChars(32)).Trim();
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
            VideoAttributes_VMGM_VOBS = new VideoAttributes(bin.ReadBytes(2));
            NumberAudioStreams_VMGM_VOBS = reverse(bin.ReadUInt16());
            AudioAttributes_VMGM_VOBS = new AudioAttributes[8];
            for (int i = 0; i < 8; ++i)
                AudioAttributes_VMGM_VOBS[i] = new AudioAttributes(bin.ReadBytes(8));
            bin.BaseStream.Position = 0x154;
            NumberSubpictureStreams_VMGM_VOBS = reverse(bin.ReadUInt16());
            SubpictureAttributes_VMGM_VOBS = new SubpictureAttributes(bin.ReadBytes(6));

            bin.BaseStream.Position = (SectorPointer_TT_SRPT * DVD_BLOCK_LEN);
            TT_SRPT_Count = reverse(bin.ReadUInt16());
            bin.BaseStream.Position = (SectorPointer_TT_SRPT * DVD_BLOCK_LEN + 8);
            TT_SRPT = new TT_SRPT_Type[TT_SRPT_Count];
            for (int i = 0; i < TT_SRPT_Count; ++i)
                TT_SRPT[i] = new TT_SRPT_Type(bin);
        }

        private System.UInt16 reverse(System.UInt16 n) { return BinaryPrimitives.ReverseEndianness(n); }
        private System.UInt32 reverse(System.UInt32 n) { return BinaryPrimitives.ReverseEndianness(n); }
        private System.UInt64 reverse(System.UInt64 n) { return BinaryPrimitives.ReverseEndianness(n); }

        ~VmgIfo()
        {
            // Explicitly tell the GC we are done with these
            if (Identifier != null) Identifier = null;
            if (ProviderID != null) ProviderID = null;
            if (AudioAttributes_VMGM_VOBS != null) AudioAttributes_VMGM_VOBS = null;
            if (SubpictureAttributes_VMGM_VOBS != null) SubpictureAttributes_VMGM_VOBS = null;
            if (TT_SRPT != null) TT_SRPT = null;
            if (VideoAttributes_VMGM_VOBS != null) VideoAttributes_VMGM_VOBS = null;
        }

    }
    #endregion

    #region Video Attributes
    public class VideoAttributes
    {
        public enum VideoCodingMode
        {
            Unknown,
            MPEG1,
            MPEG2
        }

        public enum VideoStandard
        {
            Unknown,
            NTSC,
            PAL
        }

        public enum VideoAspectRatio
        {
            Unknown,
            FourThree,
            SixteenNine
        }

        public enum VideoResolution
        {
            Unknown,
            R720x480,
            R704x480,
            R352x480,
            R352x240
        }

        public enum VideoPALSource
        {
            Unknown,
            Camera,
            Film
        }

        VideoCodingMode CodingMode;
        VideoStandard Standard;
        VideoAspectRatio AspectRatio;
        System.Boolean PanScanProhibited;
        System.Boolean LetterboxProhibited;
        System.Boolean CCLine21Field1InGOP;
        System.Boolean CCLine21Field2InGOP;
        VideoResolution Resolution;
        System.Boolean LetterBoxed;
        VideoPALSource PALSource;

        public VideoAttributes(byte[] data)
        {
            LetterboxProhibited = ((byte)(data[0] & 1) != 0) ? true : false;
            PanScanProhibited = ((byte)(data[0] & 2) != 0) ? true : false;


            switch ((byte)(data[0] >> 2 & 3))
            {
                case 0:
                    AspectRatio = VideoAspectRatio.FourThree;
                    break;
                case 3:
                    AspectRatio = VideoAspectRatio.SixteenNine;
                    break;
                default:
                    AspectRatio = VideoAspectRatio.Unknown;
                    break;
            }


            switch ((byte)(data[0] >> 4 & 3))
            {
                case 0:
                    Standard = VideoStandard.NTSC;
                    break;
                case 1:
                    Standard = VideoStandard.PAL;
                    break;
                default:
                    Standard = VideoStandard.Unknown;
                    break;
            }

            switch ((byte)(data[0] >> 6 & 3))
            {
                case 0:
                    CodingMode = VideoCodingMode.MPEG1;
                    break;
                case 1:
                    CodingMode = VideoCodingMode.MPEG2;
                    break;
                default:
                    CodingMode = VideoCodingMode.Unknown;
                    break;
            }

            PALSource = (((byte)data[1] & 1) != 0) ? VideoPALSource.Film : VideoPALSource.Camera;
            LetterBoxed = (((byte)data[1] & 4) != 0) ? true : false;

            switch ((byte)(data[1] >> 3 & 7))
            {
                case 0:
                    Resolution = VideoResolution.R720x480;
                    break;
                case 1:
                    Resolution = VideoResolution.R704x480;
                    break;
                case 2:
                    Resolution = VideoResolution.R352x480;
                    break;
                case 3:
                    Resolution = VideoResolution.R352x240;
                    break;
                default:
                    Resolution = VideoResolution.Unknown;
                    break;
            }

            CCLine21Field1InGOP = ((byte)(data[1] & 128) != 0) ? true : false;
            CCLine21Field2InGOP = ((byte)(data[1] & 64) != 0) ? true : false;
        }


    }
    #endregion


    #region Audio Attributes
    public class AudioAttributes
    {
        public byte[]? _data;

        public AudioAttributes()
        {
            if (_data == null)
                _data = new byte[8];
        }

        public AudioAttributes(byte[]? data)
        {
            _data = data;
        }   

        ~AudioAttributes()
        {
            // Explicitly tell the GC we are done with this
            if (_data != null)
                _data = null;
        }

    }
    #endregion

    #region Subpicture Attributes
    public class SubpictureAttributes
    {
        public byte[]? _data;

        public SubpictureAttributes()
        {
            if (_data == null)
                _data = new byte[6];
        }

        public SubpictureAttributes(byte[]? data)
        {
            _data = data;
        }

        ~SubpictureAttributes()
        {
            // Explicitly tell the GC we are done with this
            if (_data != null)
                _data = null;
        }


    }
    #endregion


    #region TT_SRPT
    public class TT_SRPT_Type
    {
        public System.Byte TitleType { get; private set; }
        public System.Byte NumberOfAngles { get; private set; }
        public System.UInt16 NumberOfChapters { get; private set; }      // PTTs
        public System.UInt16 ParentalManagementMask { get; private set; }
        public System.Byte VTSN { get; private set; }                   // Video Title Set Number - this is which file (VTS_nn_0.IFO) is referenced
        public System.Byte VTS_TTN { get; private set; }                // Title number insde the VTS
        public System.UInt32 StartSectorVTS { get; private set; }       // This is only used if ignoring the ISO9660 filesystem - i.e. this is the sector # on the disk where the VTS begins

        // BinaryReader position must be set before calling
        public TT_SRPT_Type(BinaryReader bin)
        {
            TitleType = bin.ReadByte();
            NumberOfAngles = bin.ReadByte();
            NumberOfChapters = reverse(bin.ReadUInt16());
            ParentalManagementMask = reverse(bin.ReadUInt16());
            VTSN = bin.ReadByte();
            VTS_TTN = bin.ReadByte();
            StartSectorVTS = reverse(bin.ReadUInt32());
        }

        private System.UInt16 reverse(System.UInt16 n) { return BinaryPrimitives.ReverseEndianness(n); }
        private System.UInt32 reverse(System.UInt32 n) { return BinaryPrimitives.ReverseEndianness(n); }
        private System.UInt64 reverse(System.UInt64 n) { return BinaryPrimitives.ReverseEndianness(n); }

    }
    #endregion





}