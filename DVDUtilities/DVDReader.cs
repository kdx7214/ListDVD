using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using static DVDUtilities.VideoAttributes;

#pragma warning disable CS8602

/*
 * 
 * Much of the data layout was obtained from http://dvd.sourceforge.net/dvdinfo/
 * Domains:  FP = First play, VMG = Video Manager, VTS = Video Title Set, VTSM = Video Title Set Menu
 * Disks can have up to 99 titles.  Each title can have one video track, 8 audio tracks, and 32 subtitle tracks
 * 
 * VIDEO_TS.VOB: first play object, usually copyright notice or menu
 * VTS_nn_0.IFO: Control and playback info for VTS nn
 * VTS_nn_0.BUP: Back copy of the IFO
 * VTS_nn_0.VOB: Menu for the title (not required)
 * VTS_nn_x.VOB: Video files (VOB) are broken up into pieces due to older limits of 1gb size.
 *               The x refers to the segment number in the file.
 * 
 * The DVD structure uses sector numbers througout. This allows a player to reference the disk without
 * using filenames.  This utility makes use of those filenames for simplicity--it's easier to read files
 * in C# than to access the physical sectors of the disk (that requires pinvoke of Win32 API calls).
 * 
 * The entries for the PGC (Program Chain) are not complete.  I was only interested in the playback time
 * and the rest of the entries are used for video players.
 * 
 * The bit rate for an audio file appears to come from the MPEG-2 stream
 * in the VOB files.  This class does not access VOB files, so that information is not available.
 * 
 * The IFO files in a DVD can only have two video streams.  The first is the menu, which is ignored here.
 * The second is the actual video the user is after.  Subtitles are localized to each VTS file, so subtitle information
 * for a given title is stored in the VtsIfo member. 
 * 
 * It's important to remember that a title is NOT a title set.  A title is CONTAINED in a title set.  Must use the
 * TT_SRPT entries in the VMG in order to get title/title set information.
 * 
 */

namespace DVDUtilities
{
    /*
     * DVDInfo:  Contains easily processed information about a DVD without having
     *           to deal with the arcane (and messy) details of the disk layout.
     * 
     */
    public class DVDInfo
    {
        private DVDReader? _dvd;
        private string? _path;
        public List<DVDTitle>? titles;

        // Constructor reads in the dvd layout details and processes it into usable data.
        //      path:  the path to the DVD layout.  This can be a drive (i.e. E:) or a path (i.e. D:\iso\movie)
        public DVDInfo(string path)
        {
            _path = path;
            _dvd = new DVDReader(path);
            titles = new List<DVDTitle>();

            for (int title = 0; title < _dvd.VmgIfo.VMG_TT_SRPT.NumberOfTitles; title++)
            {
                titles.Add(new DVDTitle(ref _dvd, title));

            }

        }

    }

    // DVDTitle:  class to hold all of the information for a single title.  Stored as an array in DVDInfo
    public class DVDTitle
    {
        private HashSet<int> used_streams;

        public int Id;
        public int ChapterCount;
        public int RuntimeHours, RuntimeMinutes, RuntimeSeconds, RuntimeFrames;
        public int TotalRuntimeInSeconds;

        public VideoAttributes? VideoParameters;
        public List<DVDAudio>? AudioTracks;
        public List<DVDSubtitle>? SubtitleTracks;

        public DVDTitle() { }

        public DVDTitle(ref DVDReader _dvd, int title)
        {
            int vtsn = _dvd.VmgIfo.VMG_TT_SRPT.entries[title].VideoTitleSetNumber - 1;
            int vts_ttn = _dvd.VmgIfo.VMG_TT_SRPT.entries[title].TitleNumberWithinVTS - 1;
            var vts_entry = _dvd.VtsIfo[vtsn];
            var pgc = vts_entry.VtsPgci.entries[vts_ttn].Pgc;
            var pgc_spst = pgc.pGC_SPST_CTLs;
            Id = title;

            used_streams = new HashSet<int>();

            VideoParameters = new VideoAttributes();
            VideoParameters = vts_entry.VideoAttributes_VTS_VOBS;

            // Some DVDs will have a title in the VTS_PGC[] that shows 3 audio streams available
            // and all have the same stream ID.  Have to remove the duplicates.
            bool[] used = new bool[8] { false, false, false, false, false, false, false, false };

            AudioTracks = new List<DVDAudio>();
            for (int x = 0; x < vts_entry.NumberAudioStreams_VTS_VOBS; x++)
            {
                //var pgc_ast = vts_entry.VtsPgci.entries[vts_ttn].Pgc.pGC_AST_CTLs[x];
                var pgc_ast = pgc.pGC_AST_CTLs[x];
                var audio_attribs = vts_entry.AudioAttributes_VTS_VOBS[x];

                if (!pgc_ast.StreamAvailable)
                    continue;
                if (used[pgc_ast.AudioStream])          // Duplicate audio tracks suck
                    continue;

                used[pgc_ast.AudioStream] = true;
                var a1 = new DVDAudio();
                a1.Id = x;
                a1.CodingMode = audio_attribs.CodingMode;
                a1.MultiChannelExtensionPresent = audio_attribs.MultiChannelExtensionPresent;
                a1.LanguageType = audio_attribs.LanguageType;
                if (a1.LanguageType == AudioAttributes.AudioLanguageType.Unspecified)
                    a1.Language = "Unknown";
                else
                    a1.Language = _dvd.langs[audio_attribs.LanguageCode];
                a1.ApplicationMode = audio_attribs.ApplicationMode;
                a1.Quantization = audio_attribs.Quantization;
                a1.SampleRate = audio_attribs.SampleRate;
                a1.Channels = audio_attribs.Channels;
                a1.CodeExtension = audio_attribs.CodeExtension;
                a1.ApplicationInfo = audio_attribs.ApplicationInfo;
                a1.DolbySurround = audio_attribs.DolbySurround;
                AudioTracks.Add(a1);
            }


            // Subtitles have a quirk.  Each entry in the PGC array for a title has 4 streams.
            // Each of these stream numbers can only be used once.  So in a title where there are multiple
            // pgc subtitle entries but all of the streams are 0, there will be a single subtitle.
            //
            // Additionally this code will generate a list of subtitles that is NOT representative of what
            // is stored in the IFO for the VTS.  This is because we need Handbrake subtitle #s, and HB
            // adds new titles for transcoding for various formats (Letterbox, Widescreen, Pan&Scan, etc.)
            // So it's possible there is only a widescreen subtitle, but this code will show for wide & letterbox
            // due to HB transcode.

            int id = 1;
            string lang = "";
            for (int xyz = 0; xyz < 8; xyz++) used[xyz] = false;
            SubtitleTracks = new List<DVDSubtitle>();
            for (int x = 0; x < 32; x++)
            {
                if (!vts_entry.VtsPgci.entries[vts_ttn].Pgc.pGC_SPST_CTLs[x].StreamAvailable) 
                    continue;

                string format = " [VOBSUB]";

                if (vts_entry.SubpictureAttributes_VTS_VOBS[x].LanguageType == SubpictureAttributes.SubpictureLanguageType.UseLanguageID)
                {
                    var s = vts_entry.SubpictureAttributes_VTS_VOBS[x].LanguageCode;
                    lang = _dvd.langs[s];
                } else
                {
                    lang = "Unknown";
                }

                if (VideoParameters.AspectRatio == VideoAspectRatio.Sixteen_Nine)
                {
                    // Always add a widescreen subtitle for 16x9 sub tracks
                    var ns = new DVDSubtitle();
                    ns.SubtitleLanguage = lang;
                    ns.SubtitleDescription = String.Format("{0}: {1} (Wide) {2}", id, lang, format);
                    ns.StreamNumber = pgc_spst[x].StreamNumberWide;
                    ns.Id = id++;
                    AddSubtitle(ns);

                    // If letterbox is allowed, add that too
                    if (vts_entry.VideoAttributes_VTS_VOBS.AutomaticLetterboxAllowed)
                    {
                        var nl = new DVDSubtitle();
                        nl.SubtitleLanguage = lang;
                        nl.SubtitleDescription = String.Format("{0}: {1} (Letterbox) {2}", id, lang, format);
                        nl.StreamNumber = pgc_spst[x].StreamNumberLetterBox;
                        nl.Id = id++;
                        AddSubtitle(nl);
                    }

                    // If Pan & Scan allowed, add it
                    if (vts_entry.VideoAttributes_VTS_VOBS.AutomaticPanScanAllowed)
                    {
                        var np = new DVDSubtitle();
                        np.SubtitleLanguage = lang;
                        np.SubtitleDescription = String.Format("{0}: {1} (Pan & Scan) {2}", id, lang, format);
                        np.StreamNumber = pgc_spst[x].StreamNumberPanScan;
                        np.Id = id++;
                        AddSubtitle(np);
                    }
                }
                else if (VideoParameters.AspectRatio == VideoAspectRatio.Four_Three)
                {
                    var n4 = new DVDSubtitle();
                    n4.SubtitleLanguage = lang;
                    n4.SubtitleDescription = String.Format("{0}: {1} (4x3) {2}", id, lang, format);
                    n4.StreamNumber = pgc_spst[x].StreamNumber4x3;
                    n4.Id = id;
                    AddSubtitle(n4);
                }
            }

            // Haven't dealt with Closed Captions yet (CC608), so have to do that here
            // CC608 subtitles will always be in the first language of the VTS
            // This ignores which title it is and adds CC608 to every title.  BAD.
            if (vts_entry.VideoAttributes_VTS_VOBS.UseCC)
            {
                var ncc = new DVDSubtitle();
                ncc.Id = id;
                ncc.SubtitleLanguage = _dvd.langs[vts_entry.SubpictureAttributes_VTS_VOBS[0].LanguageCode];
                ncc.SubtitleDescription = String.Format("{0}: {1}, Closed Caption [CC608]", id, ncc.SubtitleLanguage);
                ncc.StreamNumber = -1;
                AddSubtitle(ncc);
            }

        }

        // AddSubtitle:  adds a subtitle to the SubtitleTracks List, first searching
        //               the SubtitleTracks List to check for duplicate stream numbers
        private void AddSubtitle(DVDSubtitle s) 
        {
            if (s == null || used_streams.Contains(s.StreamNumber))
                return;

            used_streams.Add(s.StreamNumber);
            SubtitleTracks.Add(s);
        }


    }

    // DVDAudio:  Contains audio track information.  Max of 8 tracks
    //
    public class DVDAudio
    {
        public int Id;
        public AudioAttributes.AudioCodingMode CodingMode;
        public System.Boolean MultiChannelExtensionPresent;
        public AudioAttributes.AudioLanguageType LanguageType;
        public AudioAttributes.AudioApplicationMode ApplicationMode;
        public AudioAttributes.AudioQuantization Quantization;
        public System.Byte SampleRate;                  // The only option listed is 0==48kbps
        public string? Channels;
        public string? Language;
        public AudioAttributes.AudioCodeExtension CodeExtension;
        public System.Byte ApplicationInfo;             // Single byte, see web site for breakdown
        public System.Boolean DolbySurround;            // Can this track use dolby decoding
    }

    // DVDSubtitle:  Contains subtitle information for a single title.  Store as an array in DVDTitle.
    //               Length will vary based on # active subtitles (max 32)
    public class DVDSubtitle
    {
        public int Id;
        public string? SubtitleDescription;
        public string? SubtitleLanguage;
        public int StreamNumber;

        public SubpictureAttributes.SubpictureCodingMode CodingMode;
        public SubpictureAttributes.SubpictureLanguageType LanguageType;
        public string? LanguageCode;
        public SubpictureAttributes.PreferredSubpictureExtension CodeExtension;           // See System Parameter Register #19

        public DVDSubtitle()
        {
            SubtitleDescription = new string("");
            SubtitleLanguage = new String("");
            CodingMode = SubpictureAttributes.SubpictureCodingMode.None;
            LanguageType = SubpictureAttributes.SubpictureLanguageType.Unspecified;
            LanguageCode = new String("");
            CodeExtension = SubpictureAttributes.PreferredSubpictureExtension.NotSpecified;
            StreamNumber = 0;
        }


    }

    /*
     * 
     * ExtensionMethods:  Utilities used when marshalling data from disk
     * 
     */
    public class ExtensionMethods
    {
        public static System.UInt16 Get16(byte[] bytes, System.UInt32 offset)
        {
            if (bytes.Length < 1)
                return 0;
            System.UInt16 _a = bytes[offset];
            System.UInt16 _b = bytes[offset + 1];
            return (System.UInt16)((_a * 256) + _b);
        } // Get16

        public static System.UInt32 Get32(byte[] bytes, System.UInt32 offset)
        {
            if (bytes.Length < 1)
                return 0;
            System.UInt32 _a = bytes[offset];
            System.UInt32 _b = bytes[offset + 1];
            System.UInt32 _c = bytes[offset + 2];
            System.UInt32 _d = bytes[offset + 3];
            System.UInt32 _r = (_a << 24) | (_b << 16) | (_c << 8) | (_d);
            return _r;
        } // Get32

        public static System.UInt64 Get64(byte[] bytes, System.UInt32 offset)
        {
            if (bytes.Length < 1)
                return 0;

            System.UInt64 _r = (System.UInt64)bytes[offset + 0] << 56;
            _r |= (System.UInt64)bytes[offset + 1] << 48;
            _r |= (System.UInt64)bytes[offset + 2] << 40;
            _r |= (System.UInt64)bytes[offset + 3] << 32;
            _r |= (System.UInt64)bytes[offset + 4] << 24;
            _r |= (System.UInt64)bytes[offset + 5] << 16;
            _r |= (System.UInt64)bytes[offset + 6] << 8;
            _r |= (System.UInt64)bytes[offset + 7];
            return _r;
        } // Get64


        public static string GetString(byte[] bytes, System.UInt32 offset, System.UInt32 size, System.UInt32 max_size)
        {
            if ((bytes.Length < 1) || (offset + size > max_size))
                return string.Empty;

            StringBuilder _r = new StringBuilder();
            for (System.UInt32 i = offset; i < offset + size; i++)
            {
                _r.Append((Char)bytes[i]);
            }

            return _r.ToString();
        }

        // Convert BCD to Decimal as used on DVD structures
        public static System.Byte bcddec(System.Byte x)
        {
            return (System.Byte)((x & 15) + ((x >> 4) * 10));
        }

    } // public static class ExtensionMethods

    /*
     * 
     * DVDReader:  Top level class for reading the contents of a DVD
     * 
     */
    public class DVDReader
    {
        private string? _Path;                         // Root of the mount point (drive) for the DVD
        public Dictionary<string, string>? langs;

        public VMG_IFO? VmgIfo;                        // Video manager information
        public VTS_IFO[]? VtsIfo;                      // Video title set array

        /*public int GetTitleCount()
        {
            return VmgIfo.VMG_TT_SRPT.NumberOfTitles;
        }*/
        /* public int GetChapterCount(int title)
        {
            return VmgIfo.VMG_TT_SRPT.entries[title - 1].NumberOfChapters;
        }*/
        /*public int GetTitleHours(int title)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            return VtsIfo[vtsn].VtsPgci.entries[tvtsn].Pgc.Hours;
        }*/
        /*public int GetTitleMinutes(int title)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            return VtsIfo[vtsn].VtsPgci.entries[tvtsn].Pgc.Minutes;
        }*/
        /*public int GetTitleSeconds(int title)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            return VtsIfo[vtsn].VtsPgci.entries[tvtsn].Pgc.Seconds;
        }*/
        /*public int GetTitleFrames(int title)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            return VtsIfo[vtsn].VtsPgci.entries[tvtsn].Pgc.Frames;
        }*/
        /*public int GetTitleFrameRate(System.UInt16 title)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            return VtsIfo[vtsn].VtsPgci.entries[tvtsn].Pgc.FrameRate;
        }*/
        /*public int GetLengthInSeconds(int title)
        {
            return GetTitleHours(title) * 3600 + GetTitleMinutes(title) * 60 + GetTitleSeconds(title);
        }*/

        /*
        public int GetAudioStreamCount(int title)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            var pgc_ast = VtsIfo[vtsn].VtsPgci.entries[tvtsn].Pgc.pGC_AST_CTLs;

            bool[] used = new bool[8] { false, false, false, false, false, false, false, false };

            for (int h = 0; h < 8; h++)
            {
                var xt = VtsIfo[vtsn].VtsPgci.entries[tvtsn].Pgc.pGC_AST_CTLs[h];
                if (xt.StreamAvailable)
                {
                    used[xt.AudioStream] = true;
                    //Console.WriteLine(xt.ToString());
                }
            }

            int total = 0;
            for (int j = 0; j < 8; j++)
                if (used[j] == true)
                    total++;

            return total;
        }
        */


        /*public int GetSubtitleCount(int title)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            var pgc_spst = VtsIfo[vtsn].VtsPgci.entries[tvtsn].Pgc.pGC_SPST_CTLs;

            bool[] used = new bool[8] { false, false, false, false, false, false, false, false };
            for (int h = 0; h < 32; h++)
            {
                var st = VtsIfo[vtsn].VtsPgci.entries[tvtsn].Pgc.pGC_SPST_CTLs[h];
                if (st.StreamAvailable)
                {
                    Console.WriteLine(st.ToString());
                    if (st.StreamNumberPanScan > 0) used[st.StreamNumberPanScan] = true;
                    if (st.StreamNumberWide > 0) used[st.StreamNumberWide] = true;
                    if (st.StreamNumberLetterBox > 0) used[st.StreamNumberLetterBox] = true;
                    if (st.StreamNumber4x3 > 0) used[st.StreamNumber4x3] = true;
                }
            }

            int total = 0;
            foreach (var b in used)
                if (b == true) total++;

            return total;
        }*/
        /*public AudioAttributes.AudioCodingMode GetAudioStreamCoding(int title, int audiostream)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title].VideoTitleSetNumber - 1;
            int vts_ttn = VmgIfo.VMG_TT_SRPT.entries[title].TitleNumberWithinVTS - 1;
            var vts_entry = VtsIfo[vts_ttn];
            var audio_attribs = vts_entry.AudioAttributes_VTS_VOBS[audiostream];
            return audio_attribs.CodingMode;
        }*/
        /*public string GetAudioStreamLanguage(int title, int audiostream)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int vts_ttn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1; // VTS_TTN
            var pgc_ast = VtsIfo[vtsn].VtsPgci.entries[vts_ttn].Pgc.pGC_AST_CTLs[audiostream - 1];
            var audio_attribs = VtsIfo[vtsn].AudioAttributes_VTS_VOBS[pgc_ast.AudioStream];
            string s;

            // Only valid if (audio_attribs.LanguageType == AudioAttributes.AudioLanguageType.UseLanguageID)
            try
            {
                s = langs[audio_attribs.LanguageCode];
            }
            catch (KeyNotFoundException)
            {
                s = "Unknown Language";
            }
            return s;
        }*/

        /*public string GetAudioStreamChannels(int title, int audiostream)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int vts_ttn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1; // VTS_TTN
            var pgc_ast = VtsIfo[vtsn].VtsPgci.entries[vts_ttn].Pgc.pGC_AST_CTLs[audiostream - 1];
            var audio_attribs = VtsIfo[vtsn].AudioAttributes_VTS_VOBS[pgc_ast.AudioStream];
            return audio_attribs.Channels;
        }*/

        /*public AudioAttributes.AudioCodeExtension GetCodeExtension(int title, int audiostream)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int vts_ttn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1; // VTS_TTN
            var pgc_ast = VtsIfo[vtsn].VtsPgci.entries[vts_ttn].Pgc.pGC_AST_CTLs[audiostream - 1];
            var audio_attribs = VtsIfo[vtsn].AudioAttributes_VTS_VOBS[pgc_ast.AudioStream];
            return audio_attribs.CodeExtension;
        }*/

        /*public AudioAttributes.AudioApplicationMode GetAudioApplicationMode(int title, int audiostream)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int vts_ttn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1; // VTS_TTN
            var pgc_ast = VtsIfo[vtsn].VtsPgci.entries[vts_ttn].Pgc.pGC_AST_CTLs[audiostream - 1];
            var audio_attribs = VtsIfo[vtsn].AudioAttributes_VTS_VOBS[pgc_ast.AudioStream];
            return audio_attribs.ApplicationMode;
        }*/

        /*public System.Boolean GetAudioDolbySurround(int title, int audiostream)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int vts_ttn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1; // VTS_TTN
            var pgc_ast = VtsIfo[vtsn].VtsPgci.entries[vts_ttn].Pgc.pGC_AST_CTLs[audiostream - 1];
            var audio_attribs = VtsIfo[vtsn].AudioAttributes_VTS_VOBS[pgc_ast.AudioStream];
            return audio_attribs.DolbySurround;
        }*/

        /*public SubpictureAttributes.SubpictureCodingMode GetSubtitleCodingMode(int title, int subtitle)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            var sub_attribs = VtsIfo[vtsn].SubpictureAttributes_VTS_VOBS[subtitle - 1];
            return sub_attribs.CodingMode;
        }*/

        /*public SubpictureAttributes.SubpictureLanguageType GetSubtitleLanguageType(int title, int subtitle)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            var sub_attribs = VtsIfo[vtsn].SubpictureAttributes_VTS_VOBS[subtitle - 1];
            return sub_attribs.LanguageType;
        }*/

        /*public string GetSubtitleLanguage(int title, int subtitle)
        {
            string s;
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            var sub_attribs = VtsIfo[vtsn].SubpictureAttributes_VTS_VOBS[subtitle - 1];
            try
            {
                s = langs[sub_attribs.LanguageCode];
            }
            catch (KeyNotFoundException)
            {
                s = "Unknown Language";
            }
            return s;
        }*/

        /*public SubpictureAttributes.PreferredSubpictureExtension GetSubtitleCodeExtension(int title, int subtitle)
        {
            int vtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].VideoTitleSetNumber - 1;
            int tvtsn = VmgIfo.VMG_TT_SRPT.entries[title - 1].TitleNumberWithinVTS - 1;
            var sub_attribs = VtsIfo[vtsn].SubpictureAttributes_VTS_VOBS[subtitle - 1];
            return sub_attribs.CodeExtension;
        }*/

        public DVDReader(string path)
        {
            _Path = path;                              // Window drive letter (i.e. E:)
            Init(path);
            if (langs == null)
            {
                langs = new Dictionary<string, string>();
                langs.Add("\0\0", "Unknown");
                langs.Add("aa", "Afar");
                langs.Add("ab", "Abkhazian");
                langs.Add("af", "Afrikaans");
                langs.Add("am", "Amharic");
                langs.Add("ar", "Arabic");
                langs.Add("as", "Assamese");
                langs.Add("ay", "Aymara");
                langs.Add("az", "Azerbaijani");
                langs.Add("ba", "Bashkir");
                langs.Add("be", "Byelorussian");
                langs.Add("bg", "Bulgarian");
                langs.Add("bh", "Bihari");
                langs.Add("bi", "Bislama");
                langs.Add("bn", "Bengali, Bangla");
                langs.Add("bo", "Tibetan");
                langs.Add("br", "Breton");
                langs.Add("ca", "Catalan");
                langs.Add("co", "Corsican");
                langs.Add("cs", "Czech");
                langs.Add("cy", "Welsh");
                langs.Add("da", "Danish");
                langs.Add("de", "German");
                langs.Add("dz", "Bhutani");
                langs.Add("el", "Greek");
                langs.Add("en", "English");
                langs.Add("eo", "Esperanto");
                langs.Add("es", "Spanish");
                langs.Add("et", "Estonian");
                langs.Add("eu", "Basque");
                langs.Add("fa", "Persian");
                langs.Add("fi", "Finnish");
                langs.Add("fj", "Fiji");
                langs.Add("fo", "Faroese");
                langs.Add("fr", "French");
                langs.Add("fy", "Frisian");
                langs.Add("ga", "Irish");
                langs.Add("gd", "Scots Gaelic");
                langs.Add("gl", "Galician");
                langs.Add("gn", "Guarani");
                langs.Add("gu", "Gujarati");
                langs.Add("ha", "Hausa");
                langs.Add("he", "Hebrew");
                langs.Add("hi", "Hindi");
                langs.Add("hr", "Croatian");
                langs.Add("hu", "Hungarian");
                langs.Add("hy", "Armenian");
                langs.Add("ia", "Interlingua");
                langs.Add("ie", "Interlingue");
                langs.Add("ik", "Inupiak");
                langs.Add("id", "Indonesian");
                langs.Add("is", "Icelandic");
                langs.Add("it", "Italian");
                langs.Add("ja", "Japanese");
                langs.Add("jv", "Javanese");
                langs.Add("ka", "Georgian");
                langs.Add("kk", "Kazakh");
                langs.Add("kl", "Greenlandic");
                langs.Add("km", "Cambodian");
                langs.Add("kn", "Kannada");
                langs.Add("ko", "Korean");
                langs.Add("ks", "Kashmiri");
                langs.Add("ku", "Kurdish");
                langs.Add("ky", "Kirghiz");
                langs.Add("la", "Latin");
                langs.Add("lb", "Luxembourgish");
                langs.Add("ln", "Lingala");
                langs.Add("lo", "Laotian");
                langs.Add("lt", "Lithuanian");
                langs.Add("lv", "Latvian, Lettish");
                langs.Add("mg", "Malagasy");
                langs.Add("mi", "Maori");
                langs.Add("mk", "Macedonian");
                langs.Add("ml", "Malayalam");
                langs.Add("mn", "Mongolian");
                langs.Add("mo", "Moldavian");
                langs.Add("mr", "Marathi");
                langs.Add("ms", "Malay");
                langs.Add("mt", "Maltese");
                langs.Add("my", "Burmese");
                langs.Add("na", "Nauru");
                langs.Add("ne", "Nepali");
                langs.Add("nl", "Dutch");
                langs.Add("no", "Norwegian");
                langs.Add("oc", "Occitan");
                langs.Add("om", "(Afan) Oromo");
                langs.Add("or", "Oriya");
                langs.Add("pa", "Punjabi");
                langs.Add("pl", "Polish");
                langs.Add("ps", "Pashto, Pushto");
                langs.Add("pt", "Portuguese");
                langs.Add("qu", "Quechua");
                langs.Add("rm", "Rhaeto-Romance");
                langs.Add("rn", "Kirundi");
                langs.Add("ro", "Romanian");
                langs.Add("ru", "Russian");
                langs.Add("rw", "Kinyarwanda");
                langs.Add("sa", "Sanskrit");
                langs.Add("sd", "Sindhi");
                langs.Add("sg", "Sangho");
                langs.Add("sh", "Serbo-Croatian");
                langs.Add("si", "Singhalese");
                langs.Add("sk", "Slovak");
                langs.Add("sl", "Slovenian");
                langs.Add("sm", "Samoan");
                langs.Add("sn", "Shona");
                langs.Add("so", "Somali");
                langs.Add("sq", "Albanian");
                langs.Add("sr", "Serbian");
                langs.Add("ss", "Siswati");
                langs.Add("st", "Sesotho");
                langs.Add("su", "Sundanese");
                langs.Add("sv", "Swedish");
                langs.Add("sw", "Swahili");
                langs.Add("ta", "Tamil");
                langs.Add("te", "Telugu");
                langs.Add("tg", "Tajik");
                langs.Add("th", "Thai");
                langs.Add("ti", "Tigrinya");
                langs.Add("tk", "Turkmen");
                langs.Add("tl", "Tagalog");
                langs.Add("tn", "Setswana");
                langs.Add("to", "Tongan");
                langs.Add("tr", "Turkish");
                langs.Add("ts", "Tsonga");
                langs.Add("tt", "Tatar");
                langs.Add("tw", "Twi");
                langs.Add("uk", "Ukrainian");
                langs.Add("ur", "Urdu");
                langs.Add("uz", "Uzbek");
                langs.Add("vi", "Vietnamese");
                langs.Add("vo", "Volapuk");
                langs.Add("wo", "Wolof");
                langs.Add("xh", "Xhosa");
                langs.Add("yo", "Yoruba");
                langs.Add("zh", "Chinese");
                langs.Add("zu", "Zulu");
            }
        }

        public DVDReader()                             // Get rid of default constructor
        {
            throw new InvalidOperationException();
        }

        public void Init(string path)
        {
            VmgIfo = new VMG_IFO(path);
            if (VmgIfo == null) { throw new OutOfMemoryException(); }

            VtsIfo = new VTS_IFO[VmgIfo.NumberOfTitleSets];
            if (VtsIfo == null) { throw new OutOfMemoryException(); }

            for (System.UInt32 t = 0; t < VmgIfo.NumberOfTitleSets; t++)
            {
                VtsIfo[t] = new VTS_IFO(path, t);
            }
        }

    } // public class DVD

    /*
     * 
     * VTS_IFO:  Data for control and playback of an video title set (VTS)
     * 
     */
    public class VTS_IFO
    {
        public enum VTSCategory
        {
            Unspecified,
            Karaoke
        }

        private static System.UInt16 DVD_BLOCK_LEN = 2048;
        private static System.UInt32 MAX_IFO_SIZE = 256000;
        private string? _Path;

        public string? Id;
        public System.UInt32 LastSectorOfTitleSet;
        public System.UInt32 LastSectorOfIFO;
        public string? Version;
        public VTSCategory Category;
        public System.UInt32 EndByteAddress_VTS_MAT;
        public System.UInt32 StartSectorOfMenuVOB;
        public System.UInt32 StartSectorOfTitleVOB;
        public System.UInt32 SectorPointer_VTS_PTT_SRPT;        // table of titles and chapters
        public System.UInt32 SectorPointer_VTS_PGCI;            // title program chain table
        public System.UInt32 SectorPointer_VTSM_PGCI_UT;        // menu program chain table
        public System.UInt32 SectorPointer_VTS_TMAPTI;          // time map
        public System.UInt32 SectorPointer_VTSM_C_ADT;          // menu cell address table
        public System.UInt32 SectorPointer_VTSM_VOBU_ADMAP;     // menu VOBU address map
        public System.UInt32 SectorPointer_VTS_C_ADT;           // Title set cell address table
        public System.UInt32 SectorPointer_VTS_VOBU_ADMAP;      // title set VOBU address map
        public VideoAttributes? VideoAttributes_VTSM_VOBS;      // video attribs for menu
        public System.UInt16 NumberAudioStreams_VTSM_VOBS;      // number audio streams in menu
        public AudioAttributes[] AudioAttributes_VTSM_VOBS;      // audio attribs for the menu
        public System.UInt16 NumberSubpictureStreams_VTSM_VOBS; // Always zero or one
        public SubpictureAttributes? SubpictureAttributes_VTSM_VOBS; // caption attribs for menu
        public VideoAttributes? VideoAttributes_VTS_VOBS;       // video attribs for title
        public System.UInt16 NumberAudioStreams_VTS_VOBS;       // count of audio streams in the title
        public AudioAttributes[] AudioAttributes_VTS_VOBS;       // the attribs for the audio streams in the title
        public System.UInt16 NumberSubpictureStreams_VTS_VOBS;  // number of subtitles in the video title
        public SubpictureAttributes[] SubpictureAttributes_VTS_VOBS;  // number subtitles in the video title

        // There is a multichannel extension at this point in the IFO, but it's only for karaoke so it's being skipped

        public VTS_PGCI VtsPgci;

        public VTS_IFO(string path, System.UInt32 titleno) {
            System.Byte[] bytes;
            System.UInt32 _offset;

            // Title # in the array is 0 based, but the filenames are 1 based
            _Path = String.Format(@"{0}\VIDEO_TS\VTS_{1}_0.IFO", path, (titleno + 1).ToString("D2"));

            bytes = File.ReadAllBytes(_Path);

            Id = ExtensionMethods.GetString(bytes, 0, 12, MAX_IFO_SIZE);
            if (Id != "DVDVIDEO-VTS")
                throw new InvalidOperationException();

            LastSectorOfTitleSet = ExtensionMethods.Get32(bytes, 0x0c);
            LastSectorOfIFO = ExtensionMethods.Get32(bytes, 0x1c);
            Version = new string(((System.UInt16)(bytes[0x21] & 0x0f)).ToString() + "." + ((System.UInt16)((bytes[0x21] & 0xf0) >> 4)).ToString());
            Category = (bytes[0x22] == 1) ? VTSCategory.Karaoke : VTSCategory.Unspecified;
            EndByteAddress_VTS_MAT = ExtensionMethods.Get32(bytes, 0x80);
            StartSectorOfMenuVOB = ExtensionMethods.Get32(bytes, 0xc0);
            StartSectorOfTitleVOB = ExtensionMethods.Get32(bytes, 0xc4);
            SectorPointer_VTS_PTT_SRPT = ExtensionMethods.Get32(bytes, 0xc8);
            SectorPointer_VTS_PGCI = ExtensionMethods.Get32(bytes, 0xcc);
            SectorPointer_VTSM_PGCI_UT = ExtensionMethods.Get32(bytes, 0xd0);
            SectorPointer_VTS_TMAPTI = ExtensionMethods.Get32(bytes, 0xd4);
            SectorPointer_VTSM_C_ADT = ExtensionMethods.Get32(bytes, 0xd8);
            SectorPointer_VTSM_VOBU_ADMAP = ExtensionMethods.Get32(bytes, 0xdc);
            SectorPointer_VTS_C_ADT = ExtensionMethods.Get32(bytes, 0xe0);
            SectorPointer_VTS_VOBU_ADMAP = ExtensionMethods.Get32(bytes, 0xe4);
            VideoAttributes_VTSM_VOBS = new VideoAttributes(bytes, 0x100);
            NumberAudioStreams_VTSM_VOBS = ExtensionMethods.Get16(bytes, 0x102);

            AudioAttributes_VTSM_VOBS = new AudioAttributes[8];
            _offset = 0x104;
            for (int t = 0; t < 8; t++)
            {
                AudioAttributes_VTSM_VOBS[t] = new AudioAttributes();
                AudioAttributes_VTSM_VOBS[t].Init(bytes, _offset);
                _offset += 8;
            }

            NumberSubpictureStreams_VTSM_VOBS = ExtensionMethods.Get16(bytes, 0x154);
            SubpictureAttributes_VTSM_VOBS = new SubpictureAttributes(bytes, 0x156);
            VideoAttributes_VTS_VOBS = new VideoAttributes(bytes, 0x200);
            NumberAudioStreams_VTS_VOBS = ExtensionMethods.Get16(bytes, 0x202);

            AudioAttributes_VTS_VOBS = new AudioAttributes[8];
            _offset = 0x204;
            for (int t = 0; t < 8; t++)
            {
                AudioAttributes_VTS_VOBS[t] = new AudioAttributes();
                AudioAttributes_VTS_VOBS[t].Init(bytes, _offset);
                _offset += 8;

            }

            NumberSubpictureStreams_VTS_VOBS = ExtensionMethods.Get16(bytes, 0x254);

            SubpictureAttributes_VTS_VOBS = new SubpictureAttributes[32];
            _offset = 0x256;
            for (int t = 0; t < 32; t++)
            {
                SubpictureAttributes_VTS_VOBS[t] = new SubpictureAttributes(bytes, _offset);
                _offset += 6;
            }

            VtsPgci = new VTS_PGCI(bytes, SectorPointer_VTS_PGCI * DVD_BLOCK_LEN);
        }

    }

    /*
     * 
     * TT_SRPT:  Table with pointers to all of the titles on the disk.  Indexed by the title number within the VMG
     * 
     *           The entries in this table point to the actual titles, as shown on Handbrake or a dvd player,
     *           on the disk.  The NumberOfTitleSets in VMG_IFO is the number of IFO files in the VIDEO_TS directory.
     * 
     */
    public class TT_SRPT
    {
        public System.UInt16 NumberOfTitles;
        public System.UInt32 EndAddress;
        public TT_SRPT_Entry[] entries;

        public TT_SRPT(byte[] bytes, System.UInt32 offset)
        {
            NumberOfTitles = ExtensionMethods.Get16(bytes, offset);
            EndAddress = ExtensionMethods.Get32(bytes, offset + 6);            // last byte of entry?

            entries = new TT_SRPT_Entry[NumberOfTitles];
            for (System.UInt32 t = 0; t < NumberOfTitles; t++)
            {
                entries[t] = new TT_SRPT_Entry(bytes, offset + 8 + t*12);
            }
        }

        public override string ToString()
        {
            string ret = String.Format("TT_SRPT:  NumberOfTitles[{0}], EndAddress[{1}]\r\n",
                                       NumberOfTitles, EndAddress);
            foreach (var t in entries)
            {
                ret += "\t"+ t.ToString() + "\r\n";
            }

            return ret;
        }

        private TT_SRPT()               // No using the default constructor
        {
            throw new InvalidOperationException();
        }

    }

    /*
     * 
     * TT_SRPT_Entry:  One entry in the TT_SRPT table
     * 
     */
    public class TT_SRPT_Entry
    {
        public System.Byte TitleType;                          // Should break this down into an enum for readability
        public System.Byte NumberOfAngles;
        public System.UInt16 NumberOfChapters;                 // PTTs
        public System.UInt16 ParentalManagementMask;
        public System.Byte VideoTitleSetNumber;                // VTSN - reference to which VTS_nn_0.IFO this is found in - NOT THE DISK TITLE #
        public System.Byte TitleNumberWithinVTS;               // this is VTS_TTN - which title it is inside the VTS_nn_0.IFO title set
        public System.UInt32 StartSector_VTS;                  // start sector references entire disk (video_ts.ifo starts at sector 0)

        public TT_SRPT_Entry(byte[] bytes, System.UInt32 offset)
        {
            TitleType = bytes[offset + 0];
            NumberOfAngles = bytes[offset + 1];
            NumberOfChapters = ExtensionMethods.Get16(bytes, offset + 2);
            ParentalManagementMask = ExtensionMethods.Get16(bytes, offset + 4);
            VideoTitleSetNumber = bytes[offset + 6];
            TitleNumberWithinVTS = bytes[offset + 7];
            StartSector_VTS = ExtensionMethods.Get32(bytes, offset + 8);
        }

        public override string ToString()
        {
            string ret = String.Format("TT_SRPT_Entry:  TitleType[{0}], #Angles[{1}], #Chapters[{2}], ParentalMask[{3}], VTSN[{4}], TitleWithinVTS[{5}], StartSectorVTS[{6}]",
                                        TitleType, NumberOfAngles, NumberOfChapters, ParentalManagementMask, VideoTitleSetNumber, TitleNumberWithinVTS, StartSector_VTS);
            return ret;
        }

    }

    /*
     * 
     * VTS_PGCI:  Video title set program chain index.
     *            This contains the entries that determine duration of the title.
     * 
     */
    public class VTS_PGCI
    {
        private System.UInt32 offset_to_pgci;
        public System.UInt16 NumberOfPGCs;
        public System.UInt32 EndAddress;                // Address to last byte of the final PGC
        public VTS_PCGI_Entry[] entries;

        public VTS_PGCI(byte[] bytes, System.UInt32 offset)
        {
            offset_to_pgci = offset;
            NumberOfPGCs = ExtensionMethods.Get16(bytes, offset);
            EndAddress = ExtensionMethods.Get32(bytes, offset + 4);

            entries = new VTS_PCGI_Entry[NumberOfPGCs];
            for (int i = 0; i < NumberOfPGCs; i++)
            {
                entries[i] = new VTS_PCGI_Entry(bytes, (System.UInt32)(offset + 8 + i*8), offset_to_pgci);
            }
        }

        public VTS_PGCI()       // Bad constructor.  No biscuit.
        {
            throw new InvalidOperationException();
        }

        public override string ToString()
        {
            string ret = String.Format("VTS_PGCI:  NumberOfPGCs[{0}], EndAddress[{1}]\r\n",
                                        NumberOfPGCs, EndAddress);
            foreach (var t in entries)
                ret += t.ToString();
            return ret;
        }

    }

    public class VTS_PCGI_Entry
    {
        public System.Boolean EntryPGC;                     // is this the first pgc
        public System.Byte TitleNumber;
        public System.UInt16 ParentalManagementMask;
        public System.UInt32 OffsetTo_VTS_PGC;              // The offset from the start of this struct, to the PGC entry
        public PGC Pgc;

        public VTS_PCGI_Entry(byte[] bytes, System.UInt32 offset, System.UInt32 offset_to_pgci)
        {
            EntryPGC = (bytes[offset] & 128) == 128 ? true : false;
            TitleNumber = (byte)(bytes[offset] & 127);
            ParentalManagementMask = ExtensionMethods.Get16(bytes, offset + 2);
            OffsetTo_VTS_PGC = ExtensionMethods.Get32(bytes, offset + 4);
            Pgc = new PGC(bytes, offset_to_pgci + OffsetTo_VTS_PGC);

        }

        public override string ToString()
        {
            string ret = String.Format("\tVTS_PGCI_Entry:  EntryPGC[{0}], TitleNumber[{1}], Parental[{2}], Offset To VTS_PGC[{3}]\r\n",
                                        EntryPGC, TitleNumber, ParentalManagementMask, OffsetTo_VTS_PGC);
            ret += String.Format("\t\tPGC:  #Programs[{0}], #Cells[{1}], Runtime[{2}:{3}:{4}.{5}], FrameRate[{6}]\r\n",
                                 Pgc.NumberOfPrograms, Pgc.NumberOfCells, Pgc.Hours.ToString("D2"), Pgc.Minutes.ToString("D2"),
                                 Pgc.Seconds.ToString("D2"), Pgc.Frames.ToString("D2"), Pgc.FrameRate);
                                 
            return ret;
        }

    }

    /*
     * 
     * VMG_IFO:  This is the control and playback info for the whole disk
     * 
     */
    public class VMG_IFO
    {
        private static string VMG_FILE = @"\VIDEO_TS\VIDEO_TS.IFO";
        private static System.UInt16 DVD_BLOCK_LEN = 2048;
        private static System.UInt32 MAX_IFO_SIZE = 256000;
        private System.Byte[]? bytes;
        private string? _path;

        public string? Id;                                              // This is the first 12 bytes of the VIDEO_TS.IFO file
        public System.UInt32 LastSectorVMG;
        public System.UInt32 LastSectorIFO;
        public string? Version;
        public System.UInt32 VMGCategory;
        public System.UInt16 NumberOfVolumes;
        public System.UInt16 VolumeNumber;
        public System.Byte SideID;
        public System.UInt32 NumberOfTitleSets;
        public string? ProviderID;                                      // 32 byte string
        public System.UInt64 VMGPos;
        public System.UInt32 EndByteAddress_VGMI_MAT;
        public System.UInt32 StartAddress_FP_PGC;
        public System.UInt32 StartSector_Menu_VOB;
        public System.UInt32 SectorPointer_TT_SRPT;
        public System.UInt32 SectorPointer_VMGM_PGCI_UT;                // Menu program chain table
        public System.UInt32 SectorPointer_VMG_PTL_MAIT;                // Parental Management masks
        public System.UInt32 SectorPointer_VMG_VTS_ATRT;                // copies of VTS audio/subtitle attributes
        public System.UInt32 SectorPointer_VMG_TXTDT_MG;                // Text data
        public System.UInt32 SectorPointer_VMGM_C_ADT;                  // Menu cell address table
        public System.UInt32 SectorPointer_VMGM_VOBU_ADMAP;             // Menu VOBU address map
        public VideoAttributes? VideoAttributes_VMGM_VOBS;              // VOBs for the menu
        public System.UInt16 NumberAudioStreams_VMGM_VOBS;              // Number of audio streams in the menu
        public AudioAttributes[] AudioAttributes_VMGM_VOBS;             // Audio attributes for menu videos, 8 entries in array
        public System.UInt16 NumberSubpictureStreams_VMGM_VOBS;         // Number of subtitles in menu:  Can only be 0 or 1
        public SubpictureAttributes? SubpictureAttributes_VMGM_VOBS;    // Subtitle attribs for menu VOBs, 32 entries
        public TT_SRPT? VMG_TT_SRPT;                                    // The TT_SRPT table in the VMG

        // Constructor
        public VMG_IFO(string path)
        {

            _path = path + VMG_FILE;
            bytes = File.ReadAllBytes(_path);                           // Not using try so that exceptions kill the app

            Id = ExtensionMethods.GetString(bytes, 0, 12, MAX_IFO_SIZE);
            if (Id != "DVDVIDEO-VMG")
            {
                Id = "";
                throw new InvalidOperationException();
            }
            LastSectorVMG = ExtensionMethods.Get32(bytes, 0x0c);
            LastSectorIFO = ExtensionMethods.Get32(bytes, 0x1C);
            Version = new string(((System.UInt16)(bytes[0x21] & 0x0f)).ToString() + "." + ((System.UInt16)((bytes[0x21] & 0xf0) >> 4)).ToString());
            VMGCategory = ExtensionMethods.Get32(bytes,0x22);
            NumberOfVolumes = ExtensionMethods.Get16(bytes, 0x26);
            VolumeNumber = ExtensionMethods.Get16(bytes, 0x28);
            SideID = bytes[0x2a];
            NumberOfTitleSets = ExtensionMethods.Get16(bytes, 0x3e);
            ProviderID = ExtensionMethods.GetString(bytes, 0x40, 32, 32);
            VMGPos = ExtensionMethods.Get64(bytes, 0x60);
            EndByteAddress_VGMI_MAT = ExtensionMethods.Get32(bytes, 0x80);
            StartAddress_FP_PGC = ExtensionMethods.Get32(bytes, 0x84);
            StartSector_Menu_VOB = ExtensionMethods.Get32(bytes, 0xc0);
            SectorPointer_TT_SRPT = ExtensionMethods.Get32(bytes, 0xc4);
            SectorPointer_VMGM_PGCI_UT = ExtensionMethods.Get32(bytes, 0xc8);
            SectorPointer_VMG_PTL_MAIT = ExtensionMethods.Get32(bytes, 0xcc);
            SectorPointer_VMG_VTS_ATRT = ExtensionMethods.Get32(bytes, 0xd0);
            SectorPointer_VMG_TXTDT_MG = ExtensionMethods.Get32(bytes, 0xd4);
            SectorPointer_VMGM_C_ADT = ExtensionMethods.Get32(bytes, 0xd8);
            SectorPointer_VMGM_VOBU_ADMAP = ExtensionMethods.Get32(bytes, 0xdc);
            VideoAttributes_VMGM_VOBS = new VideoAttributes(bytes, 0x100);
            NumberAudioStreams_VMGM_VOBS = ExtensionMethods.Get16(bytes, 0x102);

            AudioAttributes_VMGM_VOBS = new AudioAttributes[8];
            for (System.UInt32 t = 0; t < 8; t++)
            {
                System.UInt32 _offset = 0x104;
                AudioAttributes_VMGM_VOBS[t] = new AudioAttributes();
                AudioAttributes_VMGM_VOBS[t].Init(bytes, _offset);
                _offset += 8;
            }

            NumberSubpictureStreams_VMGM_VOBS = ExtensionMethods.Get16(bytes, 0x154);
            SubpictureAttributes_VMGM_VOBS = new SubpictureAttributes(bytes, 0x156);

            VMG_TT_SRPT = new TT_SRPT(bytes, SectorPointer_TT_SRPT * DVD_BLOCK_LEN);


        }



    }

    /*
     * 
     * AudioAttributes:  Breakdown of the audio attribute field
     * 
     */

    public class AudioAttributes
    {
        private static System.UInt32 MAX_IFO_SIZE = 256000;

        public enum AudioCodingMode
        {
            Unknown,
            AC3,
            MPEG1,
            MPEG2Extended,
            LPCM,
            DTS
        }

        public enum AudioLanguageType 
        { 
            Unspecified,
            UseLanguageID
        }

        public enum AudioApplicationMode
        {
            Unspecified,
            Karaoke,
            Surround
        }

        public enum AudioQuantization
        {
            Unknown,
            NoDRC,                          // DRC = dynamic range control
            DRC,                            // DRC only valid when Coding Mode is MPEG1 or MPEG2Extended
            Mode16bps,                      // The BPS modes are only valid if Coding Mode is LPCM
            Mode20bps,
            Mode24bps
        }

        public enum AudioCodeExtension
        {
            Unspecified,
            Normal,
            VisuallyImpaired,
            DirectorsComments,
            AlternateDirectorsComments        // See SPRM#19 or PreferredSubpictureExtension class
        }

        public AudioCodingMode CodingMode;
        public System.Boolean MultiChannelExtensionPresent;
        public AudioLanguageType LanguageType;
        public AudioApplicationMode ApplicationMode;
        public AudioQuantization Quantization;
        public System.Byte SampleRate;                  // The only option listed is 0==48kbps
        public string? Channels;
        public string? LanguageCode;
        public AudioCodeExtension CodeExtension;
        public System.Byte ApplicationInfo;             // Single byte, see web site for breakdown
        public System.Boolean DolbySurround;            // Can this track use dolby decoding

        public AudioAttributes()
        {

        }
        public void Init(byte[] bytes, System.UInt32 offset)
        {
            byte x = (byte)((bytes[offset + 0] & 32 + 64 + 128) >> 5);
            switch (x)
            {
                case 0: CodingMode = AudioCodingMode.AC3; break;
                case 2: CodingMode = AudioCodingMode.MPEG1; break;
                case 3: CodingMode = AudioCodingMode.MPEG2Extended; break;
                case 4: CodingMode = AudioCodingMode.LPCM; break;
                case 6: CodingMode = AudioCodingMode.DTS; break;
                default: CodingMode = AudioCodingMode.Unknown; break;
            }
            x = (byte)((bytes[offset + 0] & 16));
            MultiChannelExtensionPresent = (x == 0) ? false : true;

            x = (byte)((bytes[offset + 0] & 4 + 8));
            LanguageType = (x == 0) ? AudioLanguageType.Unspecified : AudioLanguageType.UseLanguageID;

            x = (byte)(bytes[offset + 0] & 3);

            switch(x)
            {
                case 1:  ApplicationMode = AudioApplicationMode.Karaoke; break;
                case 2:  ApplicationMode = AudioApplicationMode.Surround; break;
                default: ApplicationMode = AudioApplicationMode.Unspecified; break;
            }

            if (CodingMode == AudioCodingMode.MPEG1 || CodingMode == AudioCodingMode.MPEG2Extended)
            {
                x = (byte)((bytes[offset + 1] & 64 + 128) >> 6);
                if (CodingMode == AudioCodingMode.MPEG1 || CodingMode == AudioCodingMode.MPEG2Extended)
                    Quantization = (x == 0) ? AudioQuantization.NoDRC : AudioQuantization.DRC;
                else if (CodingMode == AudioCodingMode.LPCM)
                {
                    switch(x)
                    {
                        case 0: Quantization = AudioQuantization.Mode16bps; break;
                        case 1: Quantization = AudioQuantization.Mode20bps; break;
                        case 2: Quantization = AudioQuantization.Mode24bps; break;
                    }
                }
            }

            x = (byte)((bytes[offset + 1] & 16 + 32) >> 4);
            SampleRate = (byte)x;

            byte y = (byte)((bytes[offset + 1] & 7) + 1);           // This field is stored as # channels - 1, so we add one to adjust
            var channel_temp = y;
            switch (channel_temp)
            {
                case 2: Channels = "2.0"; break;
                case 6: Channels = "5.1"; break;
                case 8: Channels = "7.1"; break;
                default: Channels = "1.0"; break;
            }


            LanguageCode = ExtensionMethods.GetString(bytes, offset + 2, 2, MAX_IFO_SIZE);

            // offset + 4 is reserved

            switch(bytes[offset + 5])
            {
                case 1: CodeExtension = AudioCodeExtension.Normal; break;
                case 2: CodeExtension = AudioCodeExtension.VisuallyImpaired; break;
                case 3: CodeExtension = AudioCodeExtension.DirectorsComments; break;
                case 4: CodeExtension = AudioCodeExtension.AlternateDirectorsComments; break;
                default: CodeExtension = AudioCodeExtension.Unspecified; break;
            }

            // offset + 6 is unused

            // These flags seem to be meaningless.  Maybe it comes from the VOB
            //if ((bytes[offset + 7] & 8) == 0)
            //    DolbySurround = true;
            //else
                DolbySurround = false;
        }
    }

    /*
     * 
     * VideoAttributes:  Holds the 2 byte entry for video attributes
     * 
     */

    public class VideoAttributes
    {
        public enum VideoCodingMode
        {
            MPEG1,
            MPEG2
        }

        public enum VideoStandard
        {
            NTSC,
            PAL
        }

        public enum VideoAspectRatio
        {
            Four_Three,
            Sixteen_Nine
        }

        public enum VideoResolution
        {
            Resolution_720_480,         // NTSC:  PAL uses 720x576
            Resolution_704_480,         // NTSC:  PAL uses 704x576
            Resolution_352_480,         // NTSC:  PAL uses 352x576
            Resolution_352_240          // NTSC:  PAL uses 352x288
        }

        public enum PALVideoStandard
        {
            NotPAL,
            Camera,
            Film
        }

        public VideoCodingMode CodingStandard ;
        public VideoStandard Standard;
        public VideoAspectRatio AspectRatio;
        public System.Boolean AutomaticPanScanAllowed;
        public System.Boolean AutomaticLetterboxAllowed;
        public System.Boolean CCLine21Used;
        public System.Boolean CCLine22Used;
        public System.Boolean UseCC;
        public VideoResolution Resolution;
        public System.Boolean LetterBoxed;
        public PALVideoStandard PALStandard;

        public VideoAttributes()
        {
            // This initializes a blank entry of this type
        }
        public VideoAttributes(byte[] bytes, int offset)
        {
            int x = (bytes[offset + 0] & (64 + 128)) >> 6;
            CodingStandard = (x == 0) ? VideoCodingMode.MPEG1 : VideoCodingMode.MPEG2;
            x = (bytes[offset + 0] & (16 + 32)) >> 4;
            Standard = (x == 0) ? VideoStandard.NTSC : VideoStandard.PAL;
            x = (bytes[offset + 0] & (4+8)) >> 2;
            if (x == 0) AspectRatio = VideoAspectRatio.Four_Three;
            if (x == 3) AspectRatio = VideoAspectRatio.Sixteen_Nine;
            x = bytes[offset + 0] & 2;
            AutomaticPanScanAllowed = (x == 0) ? true : false;
            x = bytes[offset + 0] & 1;
            AutomaticLetterboxAllowed = (x == 0) ? true : false;

            x = (bytes[offset + 1] & 128);
            CCLine21Used = (x == 0) ? true : false;
            x = (bytes[offset + 1] & 64);
            CCLine22Used = (x == 0) ? true : false;
            UseCC = CCLine21Used | CCLine22Used;

            x = bytes[offset + 1] & (8 + 16 + 32) >> 4;
            switch(x)
            {
                case 0: Resolution = VideoResolution.Resolution_704_480; break;
                case 1: Resolution = VideoResolution.Resolution_704_480; break;
                case 2: Resolution = VideoResolution.Resolution_352_480; break;
                case 3: Resolution = VideoResolution.Resolution_352_240; break;
            }
            x = (bytes[offset + 1] & 4) >> 2;
            LetterBoxed = (x == 0) ? false : true;
            if (Standard == VideoStandard.PAL)
            {
                x = bytes[offset + 1] & 1;
                PALStandard = (x == 0) ? PALVideoStandard.Camera : PALVideoStandard.Film;
            } else
            {
                PALStandard = PALVideoStandard.NotPAL;
            }
        }


    }

    /*
     * 
     * SubpictureAttributes:  This holds the 6 byte entry from the IFO files subtitle attributes
     * 
     */
    public class SubpictureAttributes
    {
        public enum SubpictureCodingMode
        {
            None,
            TwoBitRLE
        }

        public enum SubpictureLanguageType
        {
            Unspecified,
            UseLanguageID
        }

        public enum PreferredSubpictureExtension
        {
            NotSpecified,
            Normal,
            Large,
            Children,
            ClosedCaptions,
            LargeClosedCaptions,
            ChildrensClosedCaptions,
            Forced,
            DirectorsCommentary,
            LargeDirectorsCommentary,
            DirectorsCommentaryForChildren
        }

        public SubpictureCodingMode CodingMode;
        public SubpictureLanguageType LanguageType;
        public string? LanguageCode;
        public System.Byte ReservedForLanguageCodeExtension;
        public PreferredSubpictureExtension CodeExtension;           // See System Parameter Register #19

        public SubpictureAttributes(byte[] bytes, System.UInt32 offset)
        {
            System.UInt32 MAX_IFO_SIZE = 256000;

            byte x = (byte)((bytes[offset + 0] & 0b11100000) >> 5);
            CodingMode = (x == 0) ? SubpictureCodingMode.TwoBitRLE : SubpictureCodingMode.None;

            x = (byte)(bytes[offset + 0] &0b00000011);
            LanguageType = (x == 0) ? SubpictureLanguageType.Unspecified : SubpictureLanguageType.UseLanguageID;

            ReservedForLanguageCodeExtension = bytes[offset + 4];

            LanguageCode = ExtensionMethods.GetString(bytes, offset + 2, 2, MAX_IFO_SIZE);
            x = bytes[offset + 5];
            switch(x)
            {
                case 1: CodeExtension = PreferredSubpictureExtension.Normal; break;
                case 2: CodeExtension = PreferredSubpictureExtension.Large; break;
                case 3: CodeExtension = PreferredSubpictureExtension.Children; break;
                case 5: CodeExtension = PreferredSubpictureExtension.ClosedCaptions; break;
                case 6: CodeExtension = PreferredSubpictureExtension.LargeClosedCaptions; break;
                case 7: CodeExtension = PreferredSubpictureExtension.ChildrensClosedCaptions; break;
                case 9: CodeExtension = PreferredSubpictureExtension.Forced; break;
                case 13: CodeExtension = PreferredSubpictureExtension.DirectorsCommentary; break;
                case 14: CodeExtension = PreferredSubpictureExtension.LargeDirectorsCommentary; break;
                case 15: CodeExtension = PreferredSubpictureExtension.DirectorsCommentaryForChildren; break;
                default: CodeExtension = PreferredSubpictureExtension.NotSpecified; break;
            }

            //if (offset == 0x256) System.Diagnostics.Debugger.Break();




        }


    }


    /*
     * 
     * PGC_AST_CTL:  Audio stream control within the PGC - i.e. which audio streams are used by which title in this VTS
     * 
     */
    public class PGC_AST_CTL
    {
        public System.Boolean StreamAvailable;
        public System.Byte AudioStream;

        public PGC_AST_CTL(byte[] bytes, System.UInt32 offset)
        {
            StreamAvailable = (byte)(bytes[offset] & 0b10000000) > 0 ? true : false;
            AudioStream = (byte)(bytes[offset] & 0b00000111);       // Which audio stream to use in the VTS
        }

        public override string ToString()
        {
            string s = "";
            if (StreamAvailable)
            {
                s += "PGC_AST_CTL: Available, ";
                s += String.Format("Audio Stream[{0}]", AudioStream);
            } else
            {
                s = String.Empty;
            }
            return s;
        }



    }

    /*
     * 
     * PGC_SPST_CTL:  Subtitle stream control
     * 
     */
    public class PGC_SPST_CTL
    {
        public System.Boolean StreamAvailable;
        public System.Byte StreamNumber4x3;
        public System.Byte StreamNumberWide;
        public System.Byte StreamNumberLetterBox;
        public System.Byte StreamNumberPanScan;

        public PGC_SPST_CTL(byte[] bytes, System.UInt32 offset)
        {
            byte stream_mask = 0b00011111;
            StreamAvailable = (byte)(bytes[offset] & 0b10000000) > 0 ? true : false;
            StreamNumber4x3 = (byte)(bytes[offset] & stream_mask);
            StreamNumberWide = (byte)(bytes[offset + 1] & stream_mask);
            StreamNumberLetterBox = (byte)(bytes[offset + 2] & stream_mask);
            StreamNumberPanScan = (byte)(bytes[offset + 3] & stream_mask);
        }

        public override string ToString()
        {
            string s = "";
            if (StreamAvailable)
            {
                s += "PGC_SPST_CTL: Available, ";
                if (StreamNumber4x3 > 0) s += String.Format("Stream[{0}:4x3], ", StreamNumber4x3);
                if (StreamNumberWide > 0) s += String.Format("Stream[{0}:Wide], ", StreamNumberWide);
                if (StreamNumberLetterBox > 0) s += String.Format("Stream[{0}:LetterBox], ", StreamNumberLetterBox);
                if (StreamNumberPanScan > 0) s += String.Format("Stream[{0}:Pan&Scan", StreamNumberPanScan);
            }
            return s;
        }

    }

    /*
     * 
     * PGC:  Program Chain
     * 
     */
    public class PGC
    {
        public System.Byte NumberOfPrograms;                // This is the # chapters in this title
        public System.Byte NumberOfCells;
        public System.Byte Hours, Minutes, Seconds, Frames;
        public System.Byte FrameRate;
        public PGC_AST_CTL[]? pGC_AST_CTLs;
        public PGC_SPST_CTL[]? pGC_SPST_CTLs;

        public PGC()  // get rid of default constructor
        {
            throw new InvalidOperationException();
        }

        public PGC(byte[] bytes, System.UInt32 offset)
        {
            NumberOfPrograms = bytes[offset + 2];
            NumberOfCells = bytes[offset + 3];
            Hours = ExtensionMethods.bcddec(bytes[offset + 4]);
            Minutes = ExtensionMethods.bcddec(bytes[offset + 5]);
            Seconds = ExtensionMethods.bcddec(bytes[offset + 6]);
            Frames = ExtensionMethods.bcddec((byte)(bytes[offset + 7] & 63));
            int x = (bytes[offset + 7] & 64 + 128) >> 6;
            switch(x)
            {
                case 1: FrameRate = 25; break;
                case 3: FrameRate = 30; break;
                default: FrameRate = 0; break;
            }

            pGC_AST_CTLs = new PGC_AST_CTL[8];
            for (System.UInt32 i = 0; i < 8; i++)
            {
                pGC_AST_CTLs[i] = new PGC_AST_CTL(bytes, offset + 0x0c + i*2);
            }


            pGC_SPST_CTLs = new PGC_SPST_CTL[32];
            for (System.UInt32 i = 0; i < 32; i++) {
                pGC_SPST_CTLs[i] = new PGC_SPST_CTL(bytes, offset + 0x1c + i * 4);
            }

        }

        public override string ToString()
        {
            string ret = String.Format("PGC:  #Program[{0}], #Cells[{1}], Hours[{2}], Minutes[{3}], Seconds[{4}], Frames[{5}], FrameRate[{6}]",
                                        NumberOfPrograms, NumberOfCells, Hours, Minutes, Seconds, Frames, FrameRate);
            return ret;
        }


    }

}