﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using CUETools.Codecs.MPEG;

namespace CUETools.Codecs.MPEG.BDLPCM
{
    public class AudioDecoder : IAudioSource
    {
        public unsafe AudioDecoder(DecoderSettings settings, string path, Stream IO)
        {
            _path = path;
            _IO = IO != null ? IO : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 0x10000);
            streams = new Dictionary<ushort, TsStream>();
            frameBuffer = new byte[192];
            demuxer_channel = 0;
            _samplePos = 0;
            _sampleLen = -1;
            demux_ts_packets(null, 0);
            m_settings = settings;
        }

        public IAudioDecoderSettings Settings => m_settings;

        public void Close()
        {
            //if (_br != null)
            //{
            //    _br.Close();
            //    _br = null;
            //}
            _IO = null;
        }

        public TimeSpan Duration => Length < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Length / PCM.SampleRate);

        public long Length
        {
            get
            {
                return _sampleLen;
            }
        }

        public long Remaining
        {
            get
            {
                return _sampleLen - _samplePos;
            }
        }

        public unsafe int StreamIds
        {
            get
            {
                int res = 0;
                foreach (var s in streams)
                    if (s.Value.is_opened)
                        res++;
                return res;
            }
        }

        public long Position
        {
            get
            {
                return _samplePos;
            }
            set
            {
                if (_samplePos == value)
                {
                    return;
                }

                throw new NotSupportedException();
            }
        }

        public unsafe AudioPCMConfig PCM
        {
            get {
                if (chosenStream == null)
                {
                    if (m_settings.StreamId.HasValue)
                    {
                        if (streams.ContainsKey((ushort)m_settings.StreamId.Value))
                        {
                            var s = streams[(ushort)m_settings.StreamId.Value];
                            if (s.is_opened)
                            {
                                chosenStream = s;
                                if (chosenStream.pcm == null)
                                {
                                    demux_ts_packets(null, 0);
                                }
                                return chosenStream.pcm;
                            }
                        }
                        throw new Exception("StreamId can be " + 
                            string.Join(", ", (new List<ushort>(streams.Keys)).FindAll(pid => streams[pid].is_opened).ConvertAll(pid => pid.ToString()).ToArray()));
                    }
                    if (m_settings.Stream.HasValue)
                    {
                        foreach (var s in streams)
                        {
                            if (s.Value.is_opened && s.Value.streamId == m_settings.Stream.Value)
                            {
                                chosenStream = s.Value;
                                if (chosenStream.pcm == null)
                                {
                                    demux_ts_packets(null, 0);
                                }
                                return chosenStream.pcm;
                            }
                        }
                        throw new Exception("Stream can be " +
                            string.Join(", ", (new List<TsStream>(streams.Values)).FindAll(s => s.is_opened).ConvertAll(s => s.streamId.ToString()).ToArray()));
                    }

                    throw new Exception("multiple streams present, please specify StreamId or Stream");
                }
                return chosenStream.pcm; 
            }
        }

        public string Path { get { return _path; } }

        public unsafe int Read(AudioBuffer buff, int maxLength)
        {
            buff.Prepare(this, maxLength);
            int sampleCount;
            fixed (int* dest = &buff.Samples[0,0])
                sampleCount = demux_ts_packets(dest, buff.Length);
            buff.Length = sampleCount;
            _samplePos += sampleCount;
            return sampleCount;
        }

        unsafe int demux_ts_packets(int* dest, int maxSamples)
        {
            int samplesOffset = 0;
            while (true)
            {
                if (chosenStream != null && chosenStream.pcm != null)
                {
                    int samplesInBuffer = chosenStream.savedBufferSize / chosenStream.pcm.BlockAlign;
                    if (samplesInBuffer > 0)
                    {
                        int chunkSamples = Math.Min(samplesInBuffer, maxSamples - samplesOffset);
                        int chunkLen = chunkSamples * chosenStream.pcm.BlockAlign;
                        fixed (byte* psrc_start = &chosenStream.savedBuffer[0])
                            BDBytesToFLACSamples(
                                dest,
                                psrc_start + chosenStream.savedBufferOffset,
                                chunkSamples, chosenStream.pcm);
                        samplesOffset += chunkSamples;
                        dest += chunkSamples * chosenStream.pcm.ChannelCount;
                        chosenStream.savedBufferOffset += chunkLen;
                        chosenStream.savedBufferSize -= chunkLen;
                    }
                    
                    if (chosenStream.savedBufferSize > 0 && chosenStream.savedBufferOffset + chosenStream.pcm.BlockAlign > chosenStream.savedBuffer.Length)
                    {
                        Buffer.BlockCopy(chosenStream.savedBuffer, chosenStream.savedBufferOffset, chosenStream.savedBuffer, 0, chosenStream.savedBufferSize);
                        chosenStream.savedBufferOffset = 0;
                    }

                    if (samplesOffset >= maxSamples) return samplesOffset;
                }

                int read = _IO.Read(frameBuffer, 0, 192);
                if (read != 192)
                    break;
                //if (frameBuffer[0] == 0x47 && frameBuffer[4] != 0x47)
                //    throw new FormatException("TS, not M2TS");
                //if (frameBuffer[0] == 0x47 || frameBuffer[4] != 0x47)
                //    throw new FormatException("unknown stream type");

                fixed (byte* ptr = &frameBuffer[0])
                {
                    var fr = new FrameReader(ptr, ptr + 192);
                    TsStream s;
                    demux_ts_packet(fr, out s);
                    int dataLen = (int) fr.Length;
                    if (dataLen > 0 && s != null && (chosenStream == null || s == chosenStream))
                    {
                        int dataOffset = (int)(fr.Ptr - ptr);
                        if (s.savedBufferSize > 0)
                        {
                            int blockLen = s.pcm.BlockAlign;
                            int chunkLen = Math.Min(dataLen, blockLen - s.savedBufferSize);
                            // fr.read_bytes(svptr + s.savedBufferOffset + s.savedBufferSize, chunkLen);
                            Buffer.BlockCopy(frameBuffer, dataOffset, s.savedBuffer, s.savedBufferOffset + s.savedBufferSize, chunkLen);
                            dataOffset += chunkLen;
                            dataLen -= chunkLen;
                            chosenStream.savedBufferSize += chunkLen;
                            if (chosenStream.savedBufferSize == blockLen)
                            {
                                fixed (byte* psrc = &s.savedBuffer[s.savedBufferOffset])
                                    BDBytesToFLACSamples(dest, psrc, 1, s.pcm);
                                samplesOffset += 1;
                                dest += chosenStream.pcm.ChannelCount;
                                chosenStream.savedBufferOffset = 0;
                                chosenStream.savedBufferSize = 0;
                            }
                        }
                        if (dataLen > 0)
                        {
                            var tmp = s.savedBuffer;
                            s.savedBuffer = frameBuffer;
                            s.savedBufferOffset = dataOffset;
                            s.savedBufferSize = dataLen;
                            frameBuffer = tmp;
                            if (dest == null) return 0;
                        }
                    }
                }
            }

            return samplesOffset;
        }

        unsafe static void BDBytesToFLACSamples_24bit_6ch(int* pdest, byte* psrc, int samples, int wastedBits)
        {
            for (int i = 0; i < samples; i++)
            {
                // front left
                *(pdest++) = ((((((int)*(psrc++) << 8) + (int)*(psrc++)) << 8) + (int)*(psrc++)) << 8) >> (8 + wastedBits);
                // front right
                *(pdest++) = ((((((int)*(psrc++) << 8) + (int)*(psrc++)) << 8) + (int)*(psrc++)) << 8) >> (8 + wastedBits);
                // front center
                *(pdest++) = ((((((int)*(psrc++) << 8) + (int)*(psrc++)) << 8) + (int)*(psrc++)) << 8) >> (8 + wastedBits);
                // surround left
                pdest[1] = ((((((int)*(psrc++) << 8) + (int)*(psrc++)) << 8) + (int)*(psrc++)) << 8) >> (8 + wastedBits);
                // surround right
                pdest[2] = ((((((int)*(psrc++) << 8) + (int)*(psrc++)) << 8) + (int)*(psrc++)) << 8) >> (8 + wastedBits);
                // LFE
                pdest[0] = ((((((int)*(psrc++) << 8) + (int)*(psrc++)) << 8) + (int)*(psrc++)) << 8) >> (8 + wastedBits);
                pdest += 3;
            }
        }

        unsafe static void BDBytesToFLACSamples_24bit_noremap(int* pdest, byte* psrc, int samples, int channels, int wastedBits)
        {
            for (int i = 0; i < samples * channels; i++)
            {
                *(pdest++) = ((((((int)*(psrc++) << 8) + (int)*(psrc++)) << 8) + (int)*(psrc++)) << 8) >> (8 + wastedBits);
            }
            // if (0 != (pcm.ChannelCount & 1)) channels are padded with one extra unused channel! is it the same for wav?
        }

        unsafe static void BDBytesToFLACSamples_24bit(int* pdest, byte* psrc, int samples, int channels, int wastedBits)
        {
            switch (channels)
            {
                case 2:
                    BDBytesToFLACSamples_24bit_noremap(pdest, psrc, samples, channels, wastedBits);
                    break;
                case 4:
                    BDBytesToFLACSamples_24bit_noremap(pdest, psrc, samples, channels, wastedBits);
                    break;
                case 6:
                    BDBytesToFLACSamples_24bit_6ch(pdest, psrc, samples, wastedBits);
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        unsafe static void BDBytesToFLACSamples(int* pdest, byte* psrc, int samples, AudioPCMConfig pcm)
        {
            switch (pcm.BitsPerSample)
            {
                case 24:
                    BDBytesToFLACSamples_24bit(pdest, psrc, samples, pcm.ChannelCount, 0);
                    break;
                case 20:
                    BDBytesToFLACSamples_24bit(pdest, psrc, samples, pcm.ChannelCount, 4);
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        unsafe void process_psi_pat(TsStream s, FrameReader fr)
        {
            long len = fr.Length - 4;
            long n = len / 4;
            if (len < 0 || 0 != (len % 4))
                throw new NotSupportedException();

            for (int i = 0; i < n; i++)
            {
                ushort channel = fr.read_ushort();
                ushort pid = fr.read_ushort();
                if ((pid & 0xe000) != 0xe000)
                    throw new NotSupportedException();

                pid &= 0x1fff;

                if (demuxer_channel == 0 || demuxer_channel == channel)
                {
                    if (!streams.ContainsKey(pid))
                        streams.Add(pid, new TsStream());
                    TsStream ss = streams[pid];
                    ss.channel = channel;
                    ss.type = 0xff;
                }
            }
        }

        unsafe void process_psi_pmt(TsStream s, FrameReader fr)
        {
            // PMT (Program Map Table)
            ushort pcr_pid = fr.read_ushort();
            ushort info_len = (ushort)(fr.read_ushort() & 0x0fff);

            fr.skip(info_len);

            // CRC
            fr.Length -= 4;

            while (fr.Length > 0)
            {
                byte type = fr.read_byte();
                ushort es_pid = fr.read_ushort();

                if ((es_pid & 0xe000) != 0xe000)
                    throw new IndexOutOfRangeException();

                es_pid &= 0x1fff;

                info_len = (ushort)(fr.read_ushort() & 0x0fff);

                while (info_len > 0)
                {
                    byte tag = fr.read_byte();
                    switch (tag)
                    {
                        case 0x05: // registration descriptor
                            {
                                byte len = fr.read_byte();
                                uint rid = fr.read_uint();
                                if (rid == 0x48444D56 /* "HDMV" */ && type == 0x80 /* LPCM */)
                                {
                                    if (!streams.ContainsKey(es_pid))
                                    {
                                        streams.Add(es_pid, new TsStream());
                                        TsStream ss = streams[es_pid];
                                        if (ss.channel != s.channel || ss.type != type)
                                        {
                                            ss.channel = s.channel;
                                            ss.type = type;
                                            ss.streamId = s.streamId;
                                            ss.is_opened = true;
                                            s.streamId++;
                                        }
                                    }
                                }
                                fr.skip(len - 4);
                                info_len -= (ushort)(len + 2);
                                break;
                            }
                        default:
                            {
                                fr.skip(info_len - 1);
                                info_len = 0;
                                break;
                            }
                    }
                }
            }

            if (fr.Length > 0)
                throw new IndexOutOfRangeException();
        }

        unsafe void process_psi(TsStream s, FrameReader fr, UInt16 pid, int table)
        {
            if (0 == pid)
            {
                process_psi_pat(s, fr); // Program Association Table
                return;
            }
            //if (1 == pid)
            //{
            //    process_cat(s, ptr, end_ptr); // Conditional Access Table
            //    return;
            //}
            //if (0x11 == pid)
            //{
            //    process_sdt(s, ptr, end_ptr);
            //    return;
            //}

            if (table == 0x02)
            {
                process_psi_pmt(s, fr); // Program Map Table
                return;
            }
        }

        unsafe void process_pes_header(TsStream s, FrameReader fr)
        {
            // Packet start code prefix
            if (fr.read_byte() != 0 || fr.read_byte() != 0 || fr.read_byte() != 1)
                throw new NotSupportedException();

            s.ts_stream_id = fr.read_byte();

            int pes_len = (int)fr.read_ushort();
            int pes_type = (fr.read_byte() >> 6) & 3;

            // pes_type == 2; /* mpeg2 PES */

            byte flags1 = fr.read_byte();
            s.frame_size = fr.read_byte();
            s.frame_num++;
            s.at_packet_header = true;

            switch (flags1 & 0xc0)
            {
                case 0x80:          // PTS only
                    {
                        ulong pts = fr.read_pts();

                        if (s.dts > 0 && pts > s.dts)
                            s.frame_length = (uint)(pts - s.dts);
                        s.dts = pts;

                        if (pts > s.last_pts)
                            s.last_pts = pts;

                        if (s.first_pts == 0)
                            s.first_pts = pts;
                    }
                    break;
                case 0xc0:          // PTS,DTS
                    {
                        ulong pts = fr.read_pts();
                        ulong dts = fr.read_pts();

                        if (s.dts > 0 && dts > s.dts)
                            s.frame_length = (uint)(dts - s.dts);
                        s.dts = dts;

                        if (pts > s.last_pts)
                            s.last_pts = pts;

                        if (s.first_dts == 0)
                            s.first_dts = dts;
                    }
                    break;
            }
        }

        unsafe void BdHeader(TsStream s, FrameReader fr)
        {
            uint h = fr.read_uint();
            int pi_channels;
            int pi_channels_padding;
            int pi_bits;
            int pi_rate;

            switch( ( h & 0xf000) >> 12 )
            {
            case 1:
                pi_channels = 1;
                break;
            case 3:
                pi_channels = 2;
                break;
            case 4:
                pi_channels = 3;
                //{ AOUT_CHAN_LEFT, AOUT_CHAN_RIGHT,
                //  AOUT_CHAN_CENTER, 0 };
                break;
            case 5:
                pi_channels = 3;
                //{ AOUT_CHAN_LEFT, AOUT_CHAN_RIGHT,
                //  AOUT_CHAN_REARCENTER, 0 };
                break;
            case 6:
                pi_channels = 4;
                //{ AOUT_CHAN_LEFT, AOUT_CHAN_RIGHT,
                //  AOUT_CHAN_CENTER, AOUT_CHAN_REARCENTER, 0 };
                break;
            case 7:
                pi_channels = 4;
                //{ AOUT_CHAN_LEFT, AOUT_CHAN_RIGHT,
                //  AOUT_CHAN_REARLEFT, AOUT_CHAN_REARRIGHT, 0 };
                break;
            case 8:
                pi_channels = 5;
                //{ AOUT_CHAN_LEFT, AOUT_CHAN_RIGHT, AOUT_CHAN_CENTER,
                //  AOUT_CHAN_MIDDLELEFT, AOUT_CHAN_MIDDLERIGHT, 0 };
                break;
            case 9:
                pi_channels = 6;
                //{ AOUT_CHAN_LEFT, AOUT_CHAN_RIGHT, AOUT_CHAN_CENTER,
                //  AOUT_CHAN_REARLEFT, AOUT_CHAN_REARRIGHT, AOUT_CHAN_LFE, 0 };
                break;
            case 10:
                pi_channels = 7;
                //{ AOUT_CHAN_LEFT, AOUT_CHAN_RIGHT, AOUT_CHAN_CENTER,
                //  AOUT_CHAN_MIDDLELEFT, AOUT_CHAN_REARLEFT, AOUT_CHAN_REARRIGHT,
                //  AOUT_CHAN_MIDDLERIGHT, 0 };
                break;
            case 11:
                pi_channels = 8;
                //{ AOUT_CHAN_LEFT, AOUT_CHAN_RIGHT, AOUT_CHAN_CENTER,
                //  AOUT_CHAN_MIDDLELEFT, AOUT_CHAN_REARLEFT, AOUT_CHAN_REARRIGHT,
                //  AOUT_CHAN_MIDDLERIGHT, AOUT_CHAN_LFE, 0 };
                break;

            default:
                throw new NotSupportedException();
            }
            pi_channels_padding = pi_channels & 1;

            switch( (h >> 6) & 0x03 )
            {
            case 1:
                pi_bits = 16;
                break;
            case 2:
                pi_bits = 20;
                break;
            case 3:
                pi_bits = 24;
                break;
            default:
                throw new NotSupportedException();
            }
            
            switch( (h >> 8) & 0x0f ) 
            {
            case 1:
                pi_rate = 48000;
                break;
            case 4:
                pi_rate = 96000;
                break;
            case 5:
                pi_rate = 192000;
                break;
            default:
                throw new NotSupportedException();
            }

            if (s.pcm == null)
                s.pcm = new AudioPCMConfig(pi_bits, pi_channels, pi_rate);
        }

        unsafe void demux_ts_packet(FrameReader fr, out TsStream dataStream)
        {
            dataStream = null;

            uint timecode = fr.read_uint() & 0x3fffffff;

            byte sync = fr.read_byte(); // ts sync byte
            if (sync != 0x47) throw new FormatException("invalid packet");

            ushort pid = fr.read_ushort();
            bool transport_error = (pid & 0x8000) != 0;
            bool payload_unit_start_indicator = (pid & 0x4000) != 0;
            pid &= 0x1fff;
            if (transport_error)
                throw new FormatException("invalid packet ");

            byte flags = fr.read_byte();
            bool adaptation_field_exist = (flags & 0x20) != 0;
            bool payload_data_exist = (flags & 0x10) != 0;
            byte continuity_counter = (byte)(flags & 0x0f);

            if (pid == 0x1fff || !payload_data_exist)
            {
                return;
            }

            // skip adaptation field
            if (adaptation_field_exist)
            {
                fr.skip(fr.read_byte());
            }

            if (!streams.ContainsKey(pid))
                streams.Add(pid, new TsStream());
            TsStream s = streams[pid];

            if (0 == pid || (s.channel != 0xffff && s.type == 0xff))
            {
                // PSI (Program Specific Information)
                if (payload_unit_start_indicator)
                {
                    // begin of PSI table
                    byte pointerLen = fr.read_byte();
                    fr.skip(pointerLen);
                    byte tableId = fr.read_byte();
                    ushort sectionHeader = fr.read_ushort();
                    bool syntaxFlag = ((sectionHeader >> 15) & 1) != 0;
                    bool privateFlag = ((sectionHeader >> 14) & 1) != 0;
                    int reservedBits = (sectionHeader >> 12) & 3;
                    if (reservedBits != 3) throw new NotSupportedException();
                    int l = sectionHeader & 0x0fff;
                    if (syntaxFlag)
                    {
                        ushort extId = fr.read_ushort();
                        byte tmp8 = fr.read_byte();
                        int reserved3 = (tmp8 >> 6);
                        int version = (tmp8 >> 1) & 31;
                        bool current_flag = (tmp8 & 1) != 0;
                        byte section_num = fr.read_byte();
                        byte last_section_num = fr.read_byte();
                        l -= 5;
                    }

                    int len = (int)fr.Length;
                    if (l <= len)
                    {
                        fr.Length = l;
                        process_psi(s, fr, pid, tableId);
                        return;
                    }

                    if (l > s.psi.Length)
                        throw new FormatException("invalid packet ");
                    fixed (byte* psip = &s.psi[0])
                        fr.read_bytes(psip, len);
                    s.psi_offset = len;
                    s.psi_table = tableId;
                    s.psi_len = l;
                    return;
                }

                // next part of PSI
                if (0 == s.psi_offset)
                    throw new FormatException("invalid packet ");

                {
                    int len = (int)fr.Length;
                    if (len > s.psi.Length - s.psi_offset)
                        throw new FormatException("invalid packet ");
                    fixed (byte* psip = &s.psi[s.psi_offset])
                        fr.read_bytes(psip, len);
                    s.psi_offset += len;
                }

                if (s.psi_offset < s.psi_len)
                    return;
                fixed (byte* psip = &s.psi[0])
                {
                    var psiFr = new FrameReader(psip, psip + s.psi_len);
                    process_psi(s, psiFr, pid, s.psi_table);
                }
                return;
            }

            if (s.type != 0xff)
            {
                // PES

                if (payload_unit_start_indicator)
                {
                    s.psi_offset = 0;
                    s.psi_len = 9;
                }

                while (s.psi_offset < s.psi_len)
                {
                    int len = Math.Min((int)fr.Length, s.psi_len - s.psi_offset);
                    if (len <= 0) return;
                    fixed (byte* psip = &s.psi[s.psi_offset])
                        fr.read_bytes(psip, len);
                    s.psi_offset += len;
                    if (s.psi_len == 9)
                        s.psi_len += s.psi[8];
                }

                if (s.psi_len != 0)
                {
                    fixed (byte* psip = &s.psi[0])
                    {
                        var pesFr = new FrameReader(psip, psip + s.psi_len);
                        process_pes_header(s, pesFr);
                    }

                    s.psi_len = 0;
                    s.psi_offset = 0;
                }

                if (s.frame_num == 0)
                    return;

                //if(es_parse)
                //{
                //    switch(s.type)
                //    {
                //    case 0x1b:
                //        s.frame_num_h264.parse(fr);
                //        break;
                //    case 0x06:
                //    case 0x81:
                //    case 0x83:
                //        s.frame_num_ac3.parse(fr);
                //        break;
                //    }
                //}

                if (s.is_opened)
                {
                    if (s.at_packet_header)
                    {
                        BdHeader(s, fr);
                        s.at_packet_header = false;
                    }

                    dataStream = s;
                }
            }
        }

        string _path;
        Stream _IO;
        Dictionary<UInt16, TsStream> streams;
        byte[] frameBuffer;

        int demuxer_channel;
        TsStream chosenStream;
        long _samplePos, _sampleLen;
        DecoderSettings m_settings;
    }
}
