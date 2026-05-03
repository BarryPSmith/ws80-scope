using Microsoft.Data.Sqlite;
using SQLitePCL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    internal class DataStore
    {
        SqliteConnection _conn;
        string _fn;
        public DataStore(string fn)
        {
            _fn = fn;
            SqliteConnectionStringBuilder csb = new SqliteConnectionStringBuilder();
            csb.DataSource = fn;
            _conn = new SqliteConnection(csb.ConnectionString);
            _conn.Open();
            InitialiseDatabase();
            Task storeTask = new Task(StoreLoop, TaskCreationOptions.LongRunning);
            storeTask.Start();
        }

        ConcurrentQueue<SerialPacket> _packetsToStore = new ConcurrentQueue<SerialPacket>();
        AutoResetEvent _packetEvent = new AutoResetEvent(false);
        object _lockObj = new object();

        void StoreLoop()
        {
            while (true)
            {
                if (!_packetsToStore.TryDequeue(out var packet))
                {
                    _packetEvent.WaitOne(50);
                    continue;
                }
                var packets = new List<SerialPacket>();
                packets.Add(packet);
                while (_packetsToStore.TryDequeue(out packet))
                    packets.Add(packet);
                lock (_lockObj)
                {
                    using var tx = _conn.BeginTransaction();
                    foreach (var pkt in packets)
                        StorePacketInternal(pkt);
                    tx.Commit();
                }
            }
        }

        public async IAsyncEnumerable<SerialPacket> GetPackets(DateTimeOffset start, DateTimeOffset end)
        {
            SqliteConnectionStringBuilder csb = new SqliteConnectionStringBuilder();
            csb.DataSource = _fn;
            using var conn = new SqliteConnection(csb.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT " +
                //   0           1              2             3                 4       5       6
                "TimeStamp, OutputChannel, Inputchannel, ExcitationCount, Temperature, Trace, Edges " +
                "FROM Data " +
                "WHERE $MinTime <= TimeStamp AND TimeStamp <= $MaxTime " +
                "ORDER BY Timestamp";
            cmd.Parameters.AddWithValue("$MinTime", start.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$MaxTime", end.ToUnixTimeMilliseconds());
            var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var traceBytes = (byte[])reader["Trace"];
                var traceShorts = new short[traceBytes.Length / 2];
                Buffer.BlockCopy(traceBytes, 0, traceShorts, 0, traceBytes.Length);

                var edgeBytes = (byte[])reader["Edges"];
                var edgeShorts = new short[edgeBytes.Length / 2];
                Buffer.BlockCopy(edgeBytes, 0, edgeShorts, 0, edgeBytes.Length);
                SerialPacket pkt = new SerialPacket()
                {
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)reader["TimeStamp"]).ToLocalTime(),
                    OutputChannel = (byte)(long)reader["OutputChannel"],
                    InputChannel = (byte)(long)reader["InputChannel"],
                    ExcitationCount = (byte)(long)reader["ExcitationCount"],
                    Temperature_x10 = (short)(long)reader["Temperature"],
                    Trace = traceShorts,
                    EdgeCaptures = edgeShorts
                };
                yield return pkt;
            }
        }

        public async IAsyncEnumerable<CalibrationInfo> GetCalibrations()
        {
            SqliteConnectionStringBuilder csb = new SqliteConnectionStringBuilder();
            csb.DataSource = _fn;
            using var conn = new SqliteConnection(csb.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT " +
                "Start, End " +
                "FROM Calibrations " +
                "ORDER BY Start";
            var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long startTicks = (long)reader["Start"];
                long endTicks = (long)reader["End"];
                yield return new CalibrationInfo
                {
                    Start = DateTimeOffset.FromUnixTimeMilliseconds(startTicks).ToLocalTime(),
                    End = DateTimeOffset.FromUnixTimeMilliseconds(endTicks).ToLocalTime()
                };
            }
        }

        public async IAsyncEnumerable<SessionInfo> GetSessions()
        {
            SqliteConnectionStringBuilder csb = new SqliteConnectionStringBuilder();
            csb.DataSource = _fn;
            using var conn = new SqliteConnection(csb.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT " +
                "Name, Start, End " +
                "FROM Sessions " +
                "ORDER BY Start";
            var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var nameObj = reader["Name"];
                yield return new SessionInfo
                {
                    Name = nameObj == DBNull.Value ? null : (string)nameObj,
                    Start = DateTimeOffset.FromUnixTimeMilliseconds((long)reader["Start"]).ToLocalTime(),
                    End = DateTimeOffset.FromUnixTimeMilliseconds((long)reader["End"]).ToLocalTime()
                };
            }
        }

        public void StorePacket(SerialPacket packet)
        {
            _packetsToStore.Enqueue(packet);
            _packetEvent.Set();
        }

        void StorePacketInternal(SerialPacket packet)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Data 
(TimeStamp, OutputChannel, Inputchannel, ExcitationCount, Temperature, Trace, Edges)
VALUES
($Timestamp, $OutputChannel, $InputChannel, $ExcitationCount, $Temperature, $Trace, $EdgeCaptures)";
            cmd.Parameters.AddWithValue("$Timestamp", packet.Timestamp.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$OutputChannel", packet.OutputChannel);
            cmd.Parameters.AddWithValue("$InputChannel", packet.InputChannel);
            cmd.Parameters.AddWithValue("$ExcitationCount", packet.ExcitationCount);
            cmd.Parameters.AddWithValue("$Temperature", packet.Temperature_x10);
            byte[] byteData = new byte[packet.Trace.Length * 2];
            Buffer.BlockCopy(packet.Trace, 0, byteData, 0, packet.Trace.Length * 2);
            cmd.Parameters.AddWithValue("$Trace", byteData);
            byte[] edgeData = new byte[packet.EdgeCaptures.Length * 2];
            Buffer.BlockCopy(packet.EdgeCaptures, 0, edgeData, 0, packet.EdgeCaptures.Length * 2);
            cmd.Parameters.AddWithValue("$EdgeCaptures", edgeData);
            cmd.ExecuteNonQuery();
        }

        public void StoreCalibration(DateTimeOffset start, DateTimeOffset end)
        {
            lock (_lockObj)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Calibrations (Start, End) VALUES ($Start, $End)";
                cmd.Parameters.AddWithValue("$Start", start.ToUnixTimeMilliseconds());
                cmd.Parameters.AddWithValue("$End", end.ToUnixTimeMilliseconds());
                cmd.ExecuteNonQuery();
            }
        }

        public void StoreSession(DateTimeOffset start, DateTimeOffset end, string? name)
        {
            lock (_lockObj)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Sessions (Name, Start, End) " +
                    "VALUES ($Name, $Start, $End)";
                cmd.Parameters.AddWithValue("$Name",
                    string.IsNullOrEmpty(name) ? DBNull.Value : name);
                cmd.Parameters.AddWithValue("$Start", start.ToUnixTimeMilliseconds());
                cmd.Parameters.AddWithValue("$End", end.ToUnixTimeMilliseconds());
                cmd.ExecuteNonQuery();
            }
        }

        void InitialiseDatabase()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
PRAGMA page_size=16384;

CREATE TABLE IF NOT EXISTS Data
(
    ID INTEGER PRIMARY KEY,
    Timestamp INTEGER NOT NULL,
    OutputChannel INTEGER NOT NULL,
    InputChannel INTEGER NOT NULL,
    ExcitationCount INTEGER NOT NULL,
    Temperature INTEGER NOT NULL,
    Trace BLOB,
    Edges BLOB
);

CREATE INDEX IF NOT EXISTS idx_Data ON Data(ExcitationCount, Timestamp);

CREATE TABLE IF NOT EXISTS Calibrations
(
    ID INTEGER PRIMARY KEY,
    Start INTEGER NOT NULL,
    End INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Sessions
(
    ID INTEGER PRIMARY KEY,
    Name TEXT NULL,
    Start INTEGER NOT NULL,
    End INTEGER NOT NULL
);
";
            cmd.ExecuteNonQuery();
        }
    }
}
