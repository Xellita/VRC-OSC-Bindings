using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

class GogoController
{
    private const int VRC_RECEIVE_PORT = 9000;
    private static readonly UdpClient _udpClient = new UdpClient();
    private static readonly IPEndPoint _vrcEndpoint = new IPEndPoint(IPAddress.Loopback, VRC_RECEIVE_PORT);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_TAB = 0x09;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_LCONTROL = 0xA2;

    private static int _lastEmoteSent = 0;
    private static bool _tabWasPressed = false;

    private static float _currentFloat = 0.0f;
    private const float FLOAT_START = 0.1f;
    private const float FLOAT_MAX = 1.0f;
    private const float GROW_DURATION_SEC = 60f;
    private static DateTime? _shiftStartTime = null;

    [Flags]
    enum KeyState
    {
        None = 0,
        Pressed = 0x8000,
        Switched = 0x0001
    }

    static async Task Main()
    {
        Console.Title = "GoGo-Loco-Binder";
        Console.WriteLine("TAB для полёта (Go/VRCEmote == 123)");
        Console.WriteLine("Shift для ускорения во время полёта (Go/VRCEmote == 122 || Go/Float == 0.1 -> 1.0)");
        Console.WriteLine("Ctrl чтобы опуститься (Go/VRCEmote == 120)\n");


        while (true)
        {
            bool ctrlDown = (GetAsyncKeyState(VK_LCONTROL) & (int)KeyState.Pressed) != 0;
            bool shiftDown = (GetAsyncKeyState(VK_LSHIFT) & (int)KeyState.Pressed) != 0;
            bool tabDown = (GetAsyncKeyState(VK_TAB) & (int)KeyState.Pressed) != 0;

            bool isModeActive = (_lastEmoteSent == 123 || _lastEmoteSent == 120 || _lastEmoteSent == 122);

            if (ctrlDown && isModeActive)
            {
                if (_lastEmoteSent != 120) await SendInt("/avatar/parameters/Go/VRCEmote", 120);
                ResetShift();
            }
            else if (shiftDown && isModeActive)
            {
                if (_lastEmoteSent != 122) await SendInt("/avatar/parameters/Go/VRCEmote", 122);

                if (_shiftStartTime == null) _shiftStartTime = DateTime.Now;

                float elapsed = (float)(DateTime.Now - _shiftStartTime.Value).TotalSeconds;
                _currentFloat = Math.Min(FLOAT_MAX, FLOAT_START + (elapsed / GROW_DURATION_SEC) * (FLOAT_MAX - FLOAT_START));

                await SendFloat("/avatar/parameters/Go/Float", _currentFloat);
            }
            else if (tabDown)
            {
                if (!_tabWasPressed)
                {
                    int next = isModeActive ? 0 : 123;
                    await SendInt("/avatar/parameters/Go/VRCEmote", next);
                    _tabWasPressed = true;
                }
                ResetShift();
            }
            else
            {
                _tabWasPressed = false;

                if (_lastEmoteSent == 120 || _lastEmoteSent == 122)
                {
                    await SendInt("/avatar/parameters/Go/VRCEmote", 123);
                }

                if (_currentFloat > 0)
                {
                    _currentFloat = 0;
                    await SendFloat("/avatar/parameters/Go/Float", 0f);
                }
                ResetShift();
            }

            await Task.Delay(50);
        }
    }

    private static void ResetShift() => _shiftStartTime = null;

    static async Task SendInt(string addr, int val)
    {
        if (_lastEmoteSent == val) return;
        _lastEmoteSent = val;
        byte[] packet = BuildOscPacket(addr, "i", BitConverter.GetBytes(val).Reverse().ToArray());
        await _udpClient.SendAsync(packet, packet.Length, _vrcEndpoint);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Go/VRCEmote -> {val}");
    }

    static async Task SendFloat(string addr, float val)
    {
        byte[] packet = BuildOscPacket(addr, "f", BitConverter.GetBytes(val).Reverse().ToArray());
        await _udpClient.SendAsync(packet, packet.Length, _vrcEndpoint);
    }

    static byte[] BuildOscPacket(string addr, string tags, byte[] valBytes)
    {
        var packet = new System.Collections.Generic.List<byte>();
        byte[] addrBytes = Encoding.UTF8.GetBytes(addr);
        packet.AddRange(addrBytes);
        int padAddr = 4 - (packet.Count % 4);
        packet.AddRange(new byte[padAddr == 0 ? 4 : padAddr]);
        byte[] tagBytes = Encoding.UTF8.GetBytes("," + tags);
        packet.AddRange(tagBytes);
        int padTags = 4 - (packet.Count % 4);
        packet.AddRange(new byte[padTags == 4 ? 4 : padTags]);
        packet.AddRange(valBytes);
        return packet.ToArray();
    }
}