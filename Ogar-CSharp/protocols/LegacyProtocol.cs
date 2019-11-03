﻿using System;
using System.Collections.Generic;
using System.Text;
using Ogar_CSharp.cells;
using Ogar_CSharp.Other;
using Ogar_CSharp.sockets;
using System.Text;
using System.Linq;
using Ogar_CSharp.worlds;

namespace Ogar_CSharp.protocols
{
    public struct Legacy
    {
        public string mode;
        public int update;
        public int playersTotal;
        public int playersAlive;
        public int playersSpect;
        public int playersLimit;
    }
    public class LegacyProtocol : Protocol
    {
        public bool gotProtocol = false;
        public uint? protocol;
        public bool gotKey = false;
        public uint? Key;
        public LeaderboardType? LastleaderboardType;
        readonly byte[] PingReturn = new byte[1] { 2 };
        public bool hasProcessedQ;
        public LegacyProtocol(Connection connection) : base(connection)
        {

        }
        public override string Type => "legacy";

        public override string SubType
        {
            get
            {
                var str = "//";
                if (protocol != null)
                {
                    str = "00" + protocol.Value;
                    str = str[0..^2];
                }
                return str;
            }
        }
        public override bool Distinguishes(Reader reader)
        {
            Console.WriteLine($"length : " + reader.length);
            if (reader.length < 5) return false;
            var byt = reader.ReadByte();
            Console.WriteLine($"size : " + byt);
            if (byt != 254) return false;
            this.gotProtocol = true;
            this.protocol = reader.ReadUInt();
            Console.WriteLine("protocl : " + protocol);
            if (this.protocol < 4)
            {
                this.protocol = 4;
                Console.WriteLine($"legacy protocol: got version {this.protocol}, which is lower than 4");
            }
            return true;
        }

        public override void OnLeaderboardUpdate(LeaderboardType type, List<LeaderBoardEntry> data, LeaderBoardEntry selfData)
        {
            this.LastleaderboardType = type;
            var writer = new Writer();
            switch (type)
            {
                case LeaderboardType.FFA: FFALeaderboard(writer, data.Cast<FFALeaderboardEntry>().ToList(), (FFALeaderboardEntry)selfData, (uint)this.protocol); break;
                case LeaderboardType.Pie: PieLeaderboard(writer, data.Cast<PieLeaderboardEntry>().ToList(), (PieLeaderboardEntry)selfData, (uint)this.protocol); break;
                case LeaderboardType.Text: TextBoard(writer, data.Cast<TextLeaderBoardEntry>().ToList(), (uint)this.protocol); break;
            }
            this.Send(writer.RawBuffer);
        }

        public override void OnNewOwnedCell(PlayerCell cell)
        {
            var writer = new Writer();
            writer.WriteByte(32);
            writer.WriteUInt((uint)cell.id);
            this.Send(writer.RawBuffer);
        }

        public override void OnNewWorldBounds(Rect range, bool includeServerInfo)
        {
            var writer = new Writer();
            writer.WriteByte(64);
            writer.WriteDouble(range.x - range.w);
            writer.WriteDouble(range.y - range.h);
            writer.WriteDouble(range.x + range.w);
            writer.WriteDouble(range.y + range.h);
            if (includeServerInfo)
            {
                writer.WriteUInt(Handle.gamemode.Type);
                WriteZTString(writer, $"OgarII {Handle.Version}", (uint)protocol);
            }
            this.Send(writer.RawBuffer);
        }

        public override void OnSocketMessage(Reader reader)
        {
            var messageId = reader.ReadByte();
            if (!this.gotKey)
            {
                if (messageId != 255) return;
                if (reader.length < 5) { this.Fail(0, "Unexpected message format"); return; };
                this.gotKey = true;
                this.Key = reader.ReadUInt();
                this.connection.CreatePlayer();
                return;
            }
            switch (messageId)
            {
                case 0:
                    this.connection.spawningName = ReadZTString(reader, protocol.Value);
                    break;
                case 1:
                    this.connection.requestingSpectate = true;
                    break;
                case 16:
                    switch (reader.length)
                    {
                        case 13:
                            this.connection.mouseX = reader.ReadInt();
                            this.connection.mouseY = reader.ReadInt();
                            break;
                        case 9:
                            this.connection.mouseX = reader.ReadShort();
                            this.connection.mouseY = reader.ReadShort();
                            break;
                        case 21:
                            this.connection.mouseX = ~~(long)reader.ReadDouble();
                            this.connection.mouseY = ~~(long)(reader.ReadDouble());
                            break;
                        default: this.Fail(1003, "Unexpected message format");
                            return;
                    }
                    break;
                case 17:
                    if (this.connection.controllingMinions)
                        for (int i = 0, l = this.connection.minions.Count; i < l; i++)
                            this.connection.minions[i].splitAttempts++;
                    else this.connection.splitAttempts++;
                    break;
                case 18: this.connection.isPressingQ = true; break;
                case 19: this.connection.isPressingQ = this.hasProcessedQ = false; break;
                case 21:
                    if (this.connection.controllingMinions)
                        for (int i = 0, l = this.connection.minions.Count; i < l; i++)
                            this.connection.minions[i].ejectAttempts++;
                    else this.connection.ejectAttempts++;
                    break;
                case 22:
                    if (!this.gotKey || !Settings.minionEnableERTPControls) break;
                    for (int i = 0, l = this.connection.minions.Count; i < l; i++)
                        this.connection.minions[i].splitAttempts++;
                    break;
                case 23:
                    if (!this.gotKey || !Settings.minionEnableERTPControls) break;
                    for (int i = 0, l = this.connection.minions.Count; i < l; i++)
                        this.connection.minions[i].ejectAttempts++;
                    break;
                case 24:
                    if (!this.gotKey || !Settings.minionEnableERTPControls) break;
                    this.connection.minionsFrozen = !this.connection.minionsFrozen;
                    break;
                case 99:
                    if (reader.length < 2)
                    {
                        this.Fail(1003, "Bad message format");
                        return;
                    }
                    var flags = reader.ReadByte();
                    var skipLen = 2 * ((flags & 2) + (flags & 4) + (flags & 8));
                    if (reader.length < 2 + skipLen) {
                        Fail(1003, "Unexpected message format");
                        return;
                    }
                    reader.Skip(skipLen);
                    var message = ReadZTString(reader, this.protocol.Value);
                    this.connection.OnChatMessage(message);
                    break;
                case 254:
                    if (this.connection.hasPlayer && this.connection.Player.hasWorld)
                        this.OnStatsRequest();
                    break;
                case 255:
                    Fail(1003, "Unexpected message");
                    return;
                default:
                    Fail(1003, "Unknown message type");
                    return;
            }
        }

        public override void OnSpectatePosition(ViewArea area)
        {
            var writer = new Writer();
            writer.WriteByte(17);
            writer.WriteFloat(area.x);
            writer.WriteFloat(area.y);
            writer.WriteFloat(area.s);
            this.Send(writer.RawBuffer);
        }
        public static void WriteCellData(Writer writer, Player source, uint protocol, Cell cell, bool includeType, bool includeSize,
            bool includePos, bool includeColor, bool includeName, bool includeSkin)
        {
            Console.WriteLine(protocol);
            if (protocol == 4 || protocol == 5)
                WriteCellData4(writer, source, protocol, cell, includeType, includeSize, includePos, includeColor, includeName, includeSkin);
            else if(protocol <= 10)
                WriteCellData6(writer, source, protocol, cell, includeType, includeSize, includePos, includeColor, includeName, includeSkin);
            else if(protocol <= 21)
                WriteCellData11(writer, source, protocol, cell, includeType, includeSize, includePos, includeColor, includeName, includeSkin);
        }
        public override void OnVisibleCellUpdate(IEnumerable<Cell> add, IEnumerable<Cell> upd, IEnumerable<Cell> eat, IEnumerable<Cell> del)
        {
            var source = this.connection.Player;
            var writer = new Writer();
            writer.WriteByte(16);
            int i, l;
            l = eat.Count();
            writer.WriteUShort((ushort)l);
            foreach(var item in eat)
            {
                writer.WriteUInt((uint)item.eatenBy.id);
                writer.WriteUInt((uint)item.id);
            }
            foreach (var item in add)
            {
                WriteCellData(writer, source, this.protocol.Value, item,
                    true, true, true, true, true, true);
            }
            foreach (var item in upd)
            {
                WriteCellData(writer, source, this.protocol.Value, item,
                    false, item.sizeChanged, item.posChanged, item.colorChanged, item.nameChanged, item.skinChanged);
            }
            writer.WriteUInt(0);
            if (protocol.Value < 6)
                writer.WriteUInt((uint)l);
            else
                writer.WriteUShort((ushort)l);
           foreach(var item in del) 
                writer.WriteUInt((uint)item.id);
            this.Send(writer.RawBuffer);
        }

        public override void OnWorldReset()
        {
            var writer = new Writer();
            writer.WriteByte(18);
            this.Send(writer.RawBuffer);
            if (this.LastleaderboardType != null)
            {
                this.OnLeaderboardUpdate(LastleaderboardType.Value, new List<LeaderBoardEntry>(), null);
                this.LastleaderboardType = null;
            }
        }
        public void OnStatsRequest()
        {
            var writer = new Writer();
            writer.WriteByte(254);
            var stats = connection.Player.world.stats;
            var legacy = new Legacy { mode = stats.gamemode, update = stats.loadTime, playersTotal = stats.external, 
                playersAlive = stats.playing, playersSpect = stats.spectating, playersLimit = stats.limit };
            WriteZTString(writer, Newtonsoft.Json.JsonConvert.SerializeObject(legacy), (uint)protocol);
            Send(writer.RawBuffer);
        }
        public static void PieLeaderboard(Writer writer, List<PieLeaderboardEntry> data, PieLeaderboardEntry selfData, uint protocol)
        {
            if (protocol <= 20)
                PieLeaderboard4(writer, data, selfData, protocol);
            else if(protocol == 21)
                PieLeaderboard21(writer, data, selfData, protocol);
        }
        public static void PieLeaderboard4(Writer writer, List<PieLeaderboardEntry> data, PieLeaderboardEntry selfData, uint protocol)
        {
            writer.WriteByte(50);
            writer.WriteUInt((uint)data.Count);
            for (int i = 0, l = data.Count; i < l; i++)
                writer.WriteFloat(data[i].weight);
        }
        public static void PieLeaderboard21(Writer writer, List<PieLeaderboardEntry> data, PieLeaderboardEntry selfData, uint protocol)
        {
            writer.WriteByte(51);
            writer.WriteUInt((uint)data.Count);
            for (int i = 0, l = data.Count; i < l; i++)
            {
                writer.WriteFloat(data[i].weight);
                writer.WriteColor((uint)data[i].color);
            }
        }
        public static void TextBoard(Writer writer, List<TextLeaderBoardEntry> data, uint protocol)
        {
            if (protocol <= 13)
                TextBoard4(writer, data, protocol);
            else if (protocol <= 21)
                TextBoard14(writer, data, protocol);
        }
        public static void TextBoard4(Writer writer, List<TextLeaderBoardEntry> data, uint protocol)
        {
            writer.WriteByte(48);
            writer.WriteUInt((uint)data.Count);
            for (int i = 0, l = data.Count; i < l; i++)
                WriteZTString(writer, data[i].text, protocol);
        }
        public static void TextBoard14(Writer writer, List<TextLeaderBoardEntry> data, uint protocol)
        {
            writer.WriteByte(53);
            writer.WriteUInt((uint)data.Count);
            for (int i = 0, l = data.Count; i < l; i++)
            {
                writer.WriteByte(2);
                WriteZTString(writer, data[i].text, protocol);
            }
        }
        public static void FFALeaderboard(Writer writer, List<FFALeaderboardEntry> data, FFALeaderboardEntry selfdata, uint protocol)
        {
            if (protocol <= 10)
                FFALeaderBoard4(writer, data, selfdata, protocol);
            else if (protocol <= 21)
                FFALeaderBoard11(writer, data, selfdata, protocol);
        }
        public static void FFALeaderBoard4(Writer writer, List<FFALeaderboardEntry> data, FFALeaderboardEntry selfdata, uint protocol)
        {
            writer.WriteByte(49);
            writer.WriteUInt((uint)data.Count);
            for (int i = 0, l = data.Count; i < l; i++)
            {
                var item = data[i];
                if (protocol == 6)
                    writer.WriteUInt((uint)(item.highlighted ? 1 : 0));
                else writer.WriteUInt((uint)item.cellId);
                WriteZTString(writer, item.name, protocol);
            }
        }
        public static void FFALeaderBoard11(Writer writer, List<FFALeaderboardEntry> data, FFALeaderboardEntry selfdata, uint protocol)
        {
            writer.WriteByte((byte)(protocol >= 14 ? 53 : 51));
            for (int i = 0, l = data.Count; i < l; i++)
            {
                var item = data[i];
                if (item == selfdata)
                    writer.WriteByte(8);
                else
                {
                    writer.WriteByte(2);
                    writer.WriteUTF8String(item.name);
                }
            }
        }
        public static void WriteCellData4(Writer writer, Player source, uint protocol, Cell cell, bool includeType, bool includeSize,
            bool includePos, bool includeColor, bool includeName, bool includeSkin)
        {
            writer.WriteUInt((uint)cell.id);
            if(protocol == 4)
            {
                writer.WriteShort((short)cell.X);
                writer.WriteShort((short)cell.Y);
            }
            else
            {
                writer.WriteInt((int)cell.X);
                writer.WriteInt((int)cell.Y);
            }

            writer.WriteUShort((ushort)cell.Size);
            writer.WriteColor((uint)cell.Color);

            byte flags = 0;
            if (cell.IsSpiked) flags |= 0x01;
            if (includeSkin) flags |= 0x04;
            if (cell.IsAgitated) flags |= 0x10;
            if (cell.Type == 3) flags |= 0x20;
            writer.WriteByte(flags);

            if (includeSkin) writer.WriteUTF8String(cell.Skin);
            if (includeName) writer.WriteUTF16String(cell.Name);
            else writer.WriteUShort(0);
        }
        public static void WriteCellData11(Writer writer, Player source, uint protocol, Cell cell, bool includeType, bool includeSize, 
            bool includePos, bool includeColor, bool includeName, bool includeSkin)
        {
            writer.WriteUInt((uint)cell.id);
            writer.WriteUInt((uint)cell.Y);
            writer.WriteUInt((uint)cell.Y);
            writer.WriteUShort((ushort)cell.Size);

            byte flags = 0;
            if (cell.IsSpiked) flags |= 0x01;
            if (includeColor) flags |= 0x02;
            if (includeSkin) flags |= 0x04;
            if (includeName) flags |= 0x08;
            if (cell.IsAgitated) flags |= 0x10;
            if (cell.Type == 3) flags |= 0x20;
            if (cell.Type == 3 && cell.owner != source) flags |= 0x40;
            if (includeType && cell.Type == 1) flags |= 0x80;
            writer.WriteByte(flags);
            if (includeType && cell.Type == 1) writer.WriteByte(1);

            if (includeColor) writer.WriteColor((uint)cell.Color);
            if (includeSkin) writer.WriteUTF8String(cell.Skin);
            if (includeName) writer.WriteUTF8String(cell.Name);
        }
        public static void WriteCellData6(Writer writer, Player source, uint protocol, Cell cell, bool includeType, bool includeSize,
            bool includePos, bool includeColor, bool includeName, bool includeSkin)
        {
            writer.WriteUInt((ushort)cell.id);
            writer.WriteUInt((ushort)cell.X);
            writer.WriteUInt((ushort)cell.Y);
            writer.WriteUShort((ushort)cell.Size);

            byte flags = 0;
            if (cell.IsSpiked) flags |= 0x01;
            if (includeColor) flags |= 0x02;
            if (includeSkin) flags |= 0x04;
            if (includeName) flags |= 0x08;
            if (cell.IsAgitated) flags |= 0x10;
            if (cell.Type == 3) flags |= 0x20;
            writer.WriteByte(flags);
            Console.WriteLine(includeColor);
            if (includeColor) writer.WriteColor((uint)cell.Color);
            if (includeSkin) writer.WriteUTF8String(cell.Skin);
            if (includeName) writer.WriteUTF8String(cell.Name);
        }
        private static string ReadZTString(Reader reader, uint protocol)
        {
            if (protocol < 6)
                return reader.ReadUTF16String();
            else
                return reader.ReadUTF8String();
        }
        private static void WriteZTString(Writer writer, string value, uint protocol)
        {
            if (protocol < 6)
                writer.WriteUTF16String(value);
            else
                writer.WriteUTF8String(value);
        }
    }
}
