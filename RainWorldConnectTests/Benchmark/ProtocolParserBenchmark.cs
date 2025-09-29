using System.Diagnostics;
using System.Text;

namespace RainWorldConnectTests.Benchmark {
    // 测试用的数据包类型
    public enum PacketType {
        Login = 1,
        Message = 2,
        Update = 3
    }

    // 测试用的数据包基类
    public abstract class Packet {
        public abstract PacketType Type {
            get;
        }
        public abstract byte[] Serialize();
    }

    // 登录包
    public class LoginPacket : Packet {
        public override PacketType Type => PacketType.Login;
        public int UserId {
            get; set;
        } = 0;
        public string Username {
            get; set;
        } = "";
        public string Password {
            get; set;
        } = "";

        public override byte[] Serialize() {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write((int)Type);
            writer.Write(UserId);
            var usernameBytes = Encoding.UTF8.GetBytes(Username);
            writer.Write(usernameBytes.Length);
            writer.Write(usernameBytes);
            var passwordBytes = Encoding.UTF8.GetBytes(Password);
            writer.Write(passwordBytes.Length);
            writer.Write(passwordBytes);
            return ms.ToArray();
        }
    }

    // 消息包
    public class MessagePacket : Packet {
        public override PacketType Type => PacketType.Message;
        public int FromUserId {
            get; set;
        } = 0;
        public int ToUserId {
            get; set;
        } = 0;
        public string Content {
            get; set;
        } = "";
        public int[] AttachmentIds {
            get; set;
        } = [];

        public override byte[] Serialize() {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write((int)Type);
            writer.Write(FromUserId);
            writer.Write(ToUserId);
            var contentBytes = Encoding.UTF8.GetBytes(Content);
            writer.Write(contentBytes.Length);
            writer.Write(contentBytes);
            writer.Write(AttachmentIds.Length);
            foreach (var id in AttachmentIds) {
                writer.Write(id);
            }
            return ms.ToArray();
        }
    }

    // 更新包
    public class UpdatePacket : Packet {
        public override PacketType Type => PacketType.Update;
        public int EntityId {
            get; set;
        } = 0;
        public float PositionX {
            get; set;
        } = 0;
        public float PositionY {
            get; set;
        } = 0;
        public float PositionZ {
            get; set;
        } = 0;
        public int[] Inventory {
            get; set;
        } = [];
        public string Status {
            get; set;
        } = "";

        public override byte[] Serialize() {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write((int)Type);
            writer.Write(EntityId);
            writer.Write(PositionX);
            writer.Write(PositionY);
            writer.Write(PositionZ);
            writer.Write(Inventory.Length);
            foreach (var item in Inventory) {
                writer.Write(item);
            }
            var statusBytes = Encoding.UTF8.GetBytes(Status);
            writer.Write(statusBytes.Length);
            writer.Write(statusBytes);
            return ms.ToArray();
        }
    }

    // 长度前缀法解析器
    public class LengthPrefixParser {
        private List<byte> _buffer = new();
        private int _expectedLength = -1;

        public List<Packet> Parse(byte[] data, int offset, int count) {
            var result = new List<Packet>();
            _buffer.AddRange(data.Skip(offset).Take(count));

            while (_buffer.Count >= 4) {
                if (_expectedLength == -1) {
                    // 读取包长度
                    _expectedLength = BitConverter.ToInt32([.. _buffer.Take(4)], 0);
                }

                if (_buffer.Count >= _expectedLength + 4) {
                    // 提取完整包数据
                    var packetData = _buffer.Skip(4).Take(_expectedLength).ToArray();
                    _buffer.RemoveRange(0, _expectedLength + 4);
                    _expectedLength = -1;

                    // 解析包
                    using var ms = new MemoryStream(packetData);
                    using var reader = new BinaryReader(ms);
                    var type = (PacketType)reader.ReadInt32();
                    Packet? packet = null;

                    switch (type) {
                        case PacketType.Login:
                            packet = ParseLoginPacket(reader);
                            break;
                        case PacketType.Message:
                            packet = ParseMessagePacket(reader);
                            break;
                        case PacketType.Update:
                            packet = ParseUpdatePacket(reader);
                            break;
                    }

                    if (packet != null)
                        result.Add(packet);
                } else {
                    break; // 数据不足，等待更多数据
                }
            }

            return result;
        }

        private static LoginPacket ParseLoginPacket(BinaryReader reader) {
            var packet = new LoginPacket {
                UserId = reader.ReadInt32()
            };
            var usernameLength = reader.ReadInt32();
            packet.Username = Encoding.UTF8.GetString(reader.ReadBytes(usernameLength));
            var passwordLength = reader.ReadInt32();
            packet.Password = Encoding.UTF8.GetString(reader.ReadBytes(passwordLength));
            return packet;
        }

        private static MessagePacket ParseMessagePacket(BinaryReader reader) {
            var packet = new MessagePacket {
                FromUserId = reader.ReadInt32(),
                ToUserId = reader.ReadInt32()
            };
            var contentLength = reader.ReadInt32();
            packet.Content = Encoding.UTF8.GetString(reader.ReadBytes(contentLength));
            var arrayLength = reader.ReadInt32();
            packet.AttachmentIds = new int[arrayLength];
            for (var i = 0; i < arrayLength; i++) {
                packet.AttachmentIds[i] = reader.ReadInt32();
            }
            return packet;
        }

        private static UpdatePacket ParseUpdatePacket(BinaryReader reader) {
            var packet = new UpdatePacket {
                EntityId = reader.ReadInt32(),
                PositionX = reader.ReadSingle(),
                PositionY = reader.ReadSingle(),
                PositionZ = reader.ReadSingle()
            };
            var arrayLength = reader.ReadInt32();
            packet.Inventory = new int[arrayLength];
            for (var i = 0; i < arrayLength; i++) {
                packet.Inventory[i] = reader.ReadInt32();
            }
            var statusLength = reader.ReadInt32();
            packet.Status = Encoding.UTF8.GetString(reader.ReadBytes(statusLength));
            return packet;
        }
    }

    // 状态机解析器
    public class StateMachineParser {
        private enum ParseState {
            WaitingForPacketType,
            ParsingLogin,
            ParsingMessage,
            ParsingUpdate
        }

        private enum LoginState {
            UserId,
            UsernameLength,
            UsernameData,
            PasswordLength,
            PasswordData
        }

        private enum MessageState {
            FromUserId,
            ToUserId,
            ContentLength,
            ContentData,
            ArrayLength,
            ArrayData
        }

        private enum UpdateState {
            EntityId,
            PositionX,
            PositionY,
            PositionZ,
            ArrayLength,
            ArrayData,
            StatusLength,
            StatusData
        }

        private readonly List<byte> _buffer = [];
        private ParseState _currentState = ParseState.WaitingForPacketType;
        private PacketType _currentPacketType;
        private int _currentOffset = 0;
        private Packet? _currentPacket;
        private int _expectedLength = 0;
        private int _arrayIndex = 0;
        private object? _fieldState; // 当前字段解析状态

        public List<Packet> Parse(byte[] data, int offset, int count) {
            var result = new List<Packet>();
            _buffer.AddRange(data.Skip(offset).Take(count));

            while (_currentOffset < _buffer.Count) {
                var progress = false;
                var packetCompleted = false;

                switch (_currentState) {
                    case ParseState.WaitingForPacketType:
                        progress = ParsePacketType();
                        break;
                    case ParseState.ParsingLogin:
                        progress = ParseLogin(ref packetCompleted);
                        break;
                    case ParseState.ParsingMessage:
                        progress = ParseMessage(ref packetCompleted);
                        break;
                    case ParseState.ParsingUpdate:
                        progress = ParseUpdate(ref packetCompleted);
                        break;
                }

                if (!progress)
                    break; // 需要更多数据

                if (packetCompleted && _currentPacket != null) {
                    result.Add(_currentPacket);
                    _currentPacket = null;
                    _currentState = ParseState.WaitingForPacketType;
                    _fieldState = null;
                    // 注意：这里不重置 _currentOffset，因为我们要在最后统一清理已处理的数据
                }
            }

            // 清理已处理的数据
            if (_currentOffset > 0) {
                _buffer.RemoveRange(0, _currentOffset);
                _currentOffset = 0;
            }

            return result;
        }

        private bool ParsePacketType() {
            if (_buffer.Count - _currentOffset < 4) {
                return false;
            }
            var typeId = BitConverter.ToInt32(_buffer.ToArray(), _currentOffset);
            _currentOffset += 4;
            _currentPacketType = (PacketType)typeId;

            switch (_currentPacketType) {
                case PacketType.Login:
                    _currentState = ParseState.ParsingLogin;
                    _currentPacket = new LoginPacket();
                    _fieldState = LoginState.UserId;
                    break;
                case PacketType.Message:
                    _currentState = ParseState.ParsingMessage;
                    _currentPacket = new MessagePacket();
                    _fieldState = MessageState.FromUserId;
                    break;
                case PacketType.Update:
                    _currentState = ParseState.ParsingUpdate;
                    _currentPacket = new UpdatePacket();
                    _fieldState = UpdateState.EntityId;
                    break;
            }

            return true;
        }

        private bool ParseLogin(ref bool packetCompleted) {
            var packet = (LoginPacket)_currentPacket!;
            var state = (LoginState)_fieldState!;

            switch (state) {
                case LoginState.UserId:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    packet.UserId = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = LoginState.UsernameLength;
                    return true;
                case LoginState.UsernameLength:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    _expectedLength = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = LoginState.UsernameData;
                    return true;
                case LoginState.UsernameData:
                    if (_buffer.Count - _currentOffset < _expectedLength) {
                        return false;
                    }
                    packet.Username = Encoding.UTF8.GetString([.. _buffer], _currentOffset, _expectedLength);
                    _currentOffset += _expectedLength;
                    _fieldState = LoginState.PasswordLength;
                    return true;
                case LoginState.PasswordLength:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    _expectedLength = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = LoginState.PasswordData;
                    return true;
                case LoginState.PasswordData:
                    if (_buffer.Count - _currentOffset < _expectedLength) {
                        return false;
                    }
                    packet.Password = Encoding.UTF8.GetString([.. _buffer], _currentOffset, _expectedLength);
                    _currentOffset += _expectedLength;
                    packetCompleted = true; // 标记数据包解析完成
                    return true; // 完成
            }
            return false;
        }

        private bool ParseMessage(ref bool packetCompleted) {
            var packet = (MessagePacket)_currentPacket!;
            var state = (MessageState)_fieldState!;
            switch (state) {
                case MessageState.FromUserId:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    packet.FromUserId = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = MessageState.ToUserId;
                    return true;
                case MessageState.ToUserId:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    packet.ToUserId = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = MessageState.ContentLength;
                    return true;
                case MessageState.ContentLength:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    _expectedLength = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = MessageState.ContentData;
                    return true;
                case MessageState.ContentData:
                    if (_buffer.Count - _currentOffset < _expectedLength) {
                        return false;
                    }
                    packet.Content = Encoding.UTF8.GetString([.. _buffer], _currentOffset, _expectedLength);
                    _currentOffset += _expectedLength;
                    _fieldState = MessageState.ArrayLength;
                    return true;

                case MessageState.ArrayLength:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    _expectedLength = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    packet.AttachmentIds = new int[_expectedLength];
                    _arrayIndex = 0;
                    _fieldState = MessageState.ArrayData;
                    return true;
                case MessageState.ArrayData:
                    while (_arrayIndex < _expectedLength) {
                        if (_buffer.Count - _currentOffset < 4) {
                            return false;
                        }
                        packet.AttachmentIds[_arrayIndex] = BitConverter.ToInt32([.. _buffer], _currentOffset);
                        _currentOffset += 4;
                        _arrayIndex++;
                    }
                    packetCompleted = true; // 标记数据包解析完成
                    return true; // 完成
            }
            return false;
        }

        private bool ParseUpdate(ref bool packetCompleted) {
            var packet = (UpdatePacket)_currentPacket!;
            var state = (UpdateState)_fieldState!;
            switch (state) {
                case UpdateState.EntityId:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    packet.EntityId = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = UpdateState.PositionX;
                    return true;
                case UpdateState.PositionX:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    packet.PositionX = BitConverter.ToSingle([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = UpdateState.PositionY;
                    return true;
                case UpdateState.PositionY:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    packet.PositionY = BitConverter.ToSingle([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = UpdateState.PositionZ;
                    return true;
                case UpdateState.PositionZ:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    packet.PositionZ = BitConverter.ToSingle([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = UpdateState.ArrayLength;
                    return true;
                case UpdateState.ArrayLength:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    _expectedLength = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    packet.Inventory = new int[_expectedLength];
                    _arrayIndex = 0;
                    _fieldState = UpdateState.ArrayData;
                    return true;
                case UpdateState.ArrayData:
                    while (_arrayIndex < _expectedLength) {
                        if (_buffer.Count - _currentOffset < 4) {
                            return false;
                        }
                        packet.Inventory[_arrayIndex] = BitConverter.ToInt32([.. _buffer], _currentOffset);
                        _currentOffset += 4;
                        _arrayIndex++;
                    }
                    _fieldState = UpdateState.StatusLength;
                    return true;
                case UpdateState.StatusLength:
                    if (_buffer.Count - _currentOffset < 4) {
                        return false;
                    }
                    _expectedLength = BitConverter.ToInt32([.. _buffer], _currentOffset);
                    _currentOffset += 4;
                    _fieldState = UpdateState.StatusData;
                    return true;
                case UpdateState.StatusData:
                    if (_buffer.Count - _currentOffset < _expectedLength) {
                        return false;
                    }
                    packet.Status = Encoding.UTF8.GetString([.. _buffer], _currentOffset, _expectedLength);
                    _currentOffset += _expectedLength;
                    packetCompleted = true; // 标记数据包解析完成
                    return true; // 完成
            }
            return false;
        }
    }
    [TestClass()]
    public class ProtocolParserBenchmark {
        [TestMethod()]
        public void TestMain() {
            Console.WriteLine("TCP数据解析性能对比测试");
            Console.WriteLine("=========================");

            // 生成测试数据
            var testPackets = GenerateTestPackets(100000);
            Console.WriteLine($"生成了 {testPackets.Count} 个测试数据包");

            // 序列化所有包
            var serializedPackets = testPackets.Select(p => p.Serialize()).ToList();
            long totalBytes = serializedPackets.Sum(p => p.Length);
            Console.WriteLine($"序列化后总数据量: {totalBytes} 字节");

            // 长度前缀法 - 添加长度前缀
            var lengthPrefixedPackets = serializedPackets.Select(p => {
                var result = new byte[p.Length + 4];
                BitConverter.GetBytes(p.Length).CopyTo(result, 0);
                p.CopyTo(result, 4);
                return result;
            }).ToList();

            long totalBytesWithPrefix = lengthPrefixedPackets.Sum(p => p.Length);
            Console.WriteLine($"长度前缀法总数据量: {totalBytesWithPrefix} 字节");
            Console.WriteLine($"长度前缀开销: {totalBytesWithPrefix - totalBytes} 字节 ({(double)(totalBytesWithPrefix - totalBytes) / totalBytes * 100:F2}%)");

            var minChunkSize = 64;
            var maxChunkSize = 1024;
            var seed = new Random().Next();
            // 模拟网络传输 - 随机拆包
            var networkChunks = SimulateNetworkTransfer(lengthPrefixedPackets, new Random(seed), minChunkSize, maxChunkSize);
            Console.WriteLine($"模拟网络传输拆分成 {networkChunks.Count} 个数据块");
            var dataChunks = SimulateNetworkTransfer(serializedPackets, new Random(seed), minChunkSize, maxChunkSize);
            Console.WriteLine($"模拟网络传输拆分成 {dataChunks.Count} 个数据块");

            // 测试长度前缀法解析器
            TestParser("长度前缀法", new LengthPrefixParser(), networkChunks, testPackets.Count);

            // 测试状态机解析器
            TestParser("状态机解析法", new StateMachineParser(), dataChunks, testPackets.Count);

            Console.WriteLine("测试完成，按任意键退出...");
        }

        static List<Packet> GenerateTestPackets(int count) {
            var random = new Random(42);
            var packets = new List<Packet>();
            string[] usernames = { "alice", "bob", "charlie", "david", "eve" };
            string[] messages = { "Hello", "How are you?", "This is a test message", "Goodbye", "See you later" };
            string[] statuses = { "Online", "Offline", "Busy", "Away", "Do not disturb" };

            for (var i = 0; i < count; i++) {
                var type = (PacketType)random.Next(1, 4);
                Packet? packet = null;

                switch (type) {
                    case PacketType.Login:
                        packet = new LoginPacket {
                            UserId = random.Next(1000, 10000),
                            Username = usernames[random.Next(usernames.Length)],
                            Password = "password" + random.Next(1000)
                        };
                        break;
                    case PacketType.Message:
                        packet = new MessagePacket {
                            FromUserId = random.Next(1000, 10000),
                            ToUserId = random.Next(1000, 10000),
                            Content = messages[random.Next(messages.Length)],
                            AttachmentIds = [.. Enumerable.Range(0, random.Next(1, 6)).Select(_ => random.Next(1000, 10000))]
                        };
                        break;
                    case PacketType.Update:
                        packet = new UpdatePacket {
                            EntityId = random.Next(1000, 10000),
                            PositionX = (float)random.NextDouble() * 100,
                            PositionY = (float)random.NextDouble() * 100,
                            PositionZ = (float)random.NextDouble() * 100,
                            Inventory = [.. Enumerable.Range(0, random.Next(1, 11)).Select(_ => random.Next(1, 100))],
                            Status = statuses[random.Next(statuses.Length)]
                        };
                        break;
                }
                packets.Add(packet!);
            }
            return packets;
        }

        static List<byte[]> SimulateNetworkTransfer(List<byte[]> packets, Random random, int minChunkSize, int maxChunkSize) {
            var chunks = new List<byte[]>();
            foreach (var packet in packets) {
                var offset = 0;
                while (offset < packet.Length) {
                    var chunkSize = random.Next(minChunkSize, maxChunkSize + 1);
                    chunkSize = Math.Min(chunkSize, packet.Length - offset);
                    var chunk = new byte[chunkSize];
                    Array.Copy(packet, offset, chunk, 0, chunkSize);
                    chunks.Add(chunk);
                    offset += chunkSize;
                }
            }
            return chunks;
        }

        static void TestParser(string name, object parser, List<byte[]> dataChunks, int expectedPacketCount) {
            Console.WriteLine();
            Console.WriteLine($"测试 {name} 解析器...");

            // 准备测试
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var initialMemory = GC.GetTotalMemory(true);
            var stopwatch = Stopwatch.StartNew();

            // 解析所有数据
            var parsedPacketCount = 0;
            if (parser is LengthPrefixParser lengthPrefixParser) {
                foreach (var chunk in dataChunks) {
                    var packets = lengthPrefixParser.Parse(chunk, 0, chunk.Length);
                    parsedPacketCount += packets.Count;
                }
            } else if (parser is StateMachineParser stateMachineParser) {
                foreach (var chunk in dataChunks) {
                    var packets = stateMachineParser.Parse(chunk, 0, chunk.Length);
                    parsedPacketCount += packets.Count;
                }
            }

            stopwatch.Stop();

            // 计算内存使用
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(true);
            var memoryUsed = finalMemory - initialMemory;

            // 输出结果
            Console.WriteLine($"解析耗时: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"内存使用: {memoryUsed} 字节");
            Console.WriteLine($"解析包数量: {parsedPacketCount}/{expectedPacketCount}");

            if (parsedPacketCount != expectedPacketCount) {
                Console.WriteLine("警告: 解析包数量与预期不符!");
            }
        }
    }
}