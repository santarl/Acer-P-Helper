using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class WmiController : IDisposable
    {
        private ManagementObject? _cachedObj;
        private readonly object _lock = new();

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

        private static readonly Guid OVERLAY_EFFICIENCY = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
        private static readonly Guid OVERLAY_BALANCED = new("00000000-0000-0000-0000-000000000000");
        private static readonly Guid OVERLAY_PERFORMANCE = new("ded574b5-45a0-4f42-8737-46345c09c238");

        private byte _lastR = 0, _lastG = 150, _lastB = 255;
        private byte _brightness = 100;
        private byte _speed = 5;       
        private byte _direction = 0;   
        private int _lastMode = 3;     

        // Static mode supports genuinely independent colors per keyboard zone
        // (confirmed live: setting zone 0's bit alone changed only that zone,
        // leaving the other three untouched). Zones are 0-indexed here in
        // code (bit = 1 << zoneIndex when talking to SetGamingRgbKb); the
        // firmware's own zone numbering starts at 1, handled in
        // ApplyZoneColorRaw.
        private readonly (byte R, byte G, byte B)[] _zoneColors =
        {
            (0, 150, 255), (0, 150, 255), (0, 150, 255), (0, 150, 255)
        };

        public (byte R, byte G, byte B) GetZoneColor(int zoneIndex) =>
            zoneIndex >= 0 && zoneIndex < 4 ? _zoneColors[zoneIndex] : ((byte)0, (byte)0, (byte)0);

        private byte _customCpuFanSpeed = 50;
        private byte _customGpuFanSpeed = 50;

        public byte LastR => _lastR;
        public byte LastG => _lastG;
        public byte LastB => _lastB;
        public byte Brightness => _brightness;
        public byte Speed => _speed;
        public byte Direction => _direction;
        public int LastRgbMode => _lastMode;
        public byte CustomCpuFanSpeed => _customCpuFanSpeed;
        public byte CustomGpuFanSpeed => _customGpuFanSpeed;

        private ManagementObject? GetWmiObject()
        {
            lock (_lock)
            {
                if (_cachedObj != null) return _cachedObj;
                try
                {
                    using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGamingFunction");
                    using var results = searcher.Get();
                    _cachedObj = results.Cast<ManagementObject>().FirstOrDefault();
                }
                catch { _cachedObj = null; }
                return _cachedObj;
            }
        }

        private void InvalidateCache()
        {
            lock (_lock)
            {
                try { _cachedObj?.Dispose(); } catch { }
                _cachedObj = null;
            }
        }

        private (bool success, ulong output) SendCommand(string method, ulong input)
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return (false, 0);

                using var inParams = obj.GetMethodParameters(method);
                inParams["gmInput"] = input;
                using var outParams = obj.InvokeMethod(method, inParams, null);
                ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                return ((result & 0xFF) == 0, result);
            }
            catch
            {
                InvalidateCache();
                return (false, 0);
            }
        }

        private bool SendLedCommand(byte[] payload)
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return false;

                using var inParams = obj.GetMethodParameters("SetGamingKBBacklight");
                inParams["gmInput"] = payload;
                using var outParams = obj.InvokeMethod("SetGamingKBBacklight", inParams, null);
                ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                return (result & 0xFF) == 0;
            }
            catch
            {
                InvalidateCache();
                return false;
            }
        }

        private bool SendRgbKbCommand(uint zone, byte r, byte g, byte b)
        {
            ulong payload = zone | ((ulong)r << 8) | ((ulong)g << 16) | ((ulong)b << 24);
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return false;

                using var inParams = obj.GetMethodParameters("SetGamingRgbKb");
                inParams["gmInput"] = payload;
                using var outParams = obj.InvokeMethod("SetGamingRgbKb", inParams, null);
                ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                return (result & 0xFF) == 0;
            }
            catch
            {
                InvalidateCache();
                return false;
            }
        }

        private int GetSensorReading(ulong sensorId)
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return 0;

                using var inParams = obj.GetMethodParameters("GetGamingSysInfo");
                inParams["gmInput"] = (ulong)(0x0001 | (sensorId << 8));
                using var outParams = obj.InvokeMethod("GetGamingSysInfo", inParams, null);
                ulong raw = Convert.ToUInt64(outParams["gmOutput"]);
                if ((raw & 0xFF) == 0) return (int)((raw >> 8) & 0xFFFF);
            }
            catch (ManagementException) { InvalidateCache(); }
            catch (COMException) { InvalidateCache(); }
            catch { }
            return 0;
        }

        public void SetPowerMode(byte mode)
        {
            SendCommand("SetGamingMiscSetting", (ulong)0x0B | ((ulong)mode << 8));
            SyncWindowsPowerMode(mode);
        }

        public void SetFanBehavior(byte mode)
        {
            SendCommand("SetGamingFanBehavior", (ulong)(0x09 | ((ulong)mode << 16) | ((ulong)mode << 22)));

            if (mode == 0x03)
                SetFanSpeed(_customCpuFanSpeed, _customGpuFanSpeed);
        }

        public bool SetFanSpeed(byte cpuSpeed, byte gpuSpeed)
        {
            _customCpuFanSpeed = cpuSpeed;
            _customGpuFanSpeed = gpuSpeed;

            var (cpuOk, _) = SendCommand("SetGamingFanSpeed", 0x01UL | ((ulong)cpuSpeed << 8));
            var (gpuOk, _) = SendCommand("SetGamingFanSpeed", 0x04UL | ((ulong)gpuSpeed << 8));
            return cpuOk && gpuOk;
        }

        public bool SetCpuFanSpeed(byte speed)
        {
            _customCpuFanSpeed = speed;
            var (ok, _) = SendCommand("SetGamingFanSpeed", 0x01UL | ((ulong)speed << 8));
            return ok;
        }

        public bool SetGpuFanSpeed(byte speed)
        {
            _customGpuFanSpeed = speed;
            var (ok, _) = SendCommand("SetGamingFanSpeed", 0x04UL | ((ulong)speed << 8));
            return ok;
        }

        public void SetRgbMode(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction)
        {
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = brightness;
            _speed = speed;
            _direction = direction;
            _lastMode = mode;
            ApplyLightingMode(mode);
        }

        public void SetBrightness(byte brightness)
        {
            _brightness = brightness;
            if (_lastMode == 0) _staticBrightnessPct = brightness;
            ApplyLightingMode(_lastMode);
        }

        public void SetSpeed(byte speed)
        {
            _speed = speed;
            ApplyLightingMode(_lastMode);
        }

        public void SetDirection(byte direction)
        {
            _direction = direction;
            ApplyLightingMode(_lastMode);
        }

        private bool ApplyZoneColorRaw(int zoneIndex, byte r, byte g, byte b)
        {
            if (zoneIndex < 0 || zoneIndex > 3) return false;
            _zoneColors[zoneIndex] = (r, g, b);
            // "zone" is a 4-bit mask (bit0-3 = zones A-D), NOT a linear index.
            // A loop sending raw values 1,2,3,4 only ever set bits 0,1,(0+1),2 -
            // bit3 (zone D, value 8) was never reached, which is exactly why
            // only 3 of 4 zones changed. One bit per call addresses exactly
            // one zone independently.
            var (sr, sg, sb) = ScaleForStaticBrightness(r, g, b);
            return SendRgbKbCommand((uint)(1 << zoneIndex), sr, sg, sb);
        }

        // SetGamingRgbKb's {zone, R, G, B} struct has no separate brightness
        // field (matching the Linux driver's struct - brightness isn't a
        // firmware register for static zones the way it is for effect
        // modes). So brightness here is applied by scaling the RGB values
        // before sending, while _zoneColors keeps the user's true chosen
        // color unscaled - moving the slider back to 100% exactly restores
        // the original color instead of compounding repeated dims.
        private byte _staticBrightnessPct = 100;
        public byte StaticBrightnessPct => _staticBrightnessPct;

        private static (byte, byte, byte) ScaleForStaticBrightness(byte r, byte g, byte b, byte pct)
        {
            double f = Math.Clamp(pct, (byte)0, (byte)100) / 100.0;
            return ((byte)(r * f), (byte)(g * f), (byte)(b * f));
        }

        private (byte, byte, byte) ScaleForStaticBrightness(byte r, byte g, byte b) =>
            ScaleForStaticBrightness(r, g, b, _staticBrightnessPct);

        public bool SetStaticBrightness(byte percent)
        {
            _staticBrightnessPct = Math.Clamp(percent, (byte)0, (byte)100);
            bool allOk = true;
            for (int zone = 0; zone < 4; zone++)
            {
                var (r, g, b) = _zoneColors[zone];
                allOk &= ApplyZoneColorRaw(zone, r, g, b);
                Thread.Sleep(15);
            }
            return allOk;
        }

        public bool SetZoneColor(int zoneIndex, byte r, byte g, byte b)
        {
            _lastMode = 0;
            SendCommand("SetGamingLEDBehavior", 0x07ul);
            Thread.Sleep(50);
            return ApplyZoneColorRaw(zoneIndex, r, g, b);
        }

        public bool SetStaticColor(byte r, byte g, byte b, byte brightness)
        {
            // Applies the same color to all four zones - used by the single
            // "quick color" picker for people who don't want to fuss with
            // per-zone colors. SetZoneColor is the per-zone entry point.
            _brightness = brightness;
            _staticBrightnessPct = brightness;
            _lastMode = 0;

            SendCommand("SetGamingLEDBehavior", 0x07ul);
            Thread.Sleep(50);

            bool allOk = true;
            for (int zone = 0; zone < 4; zone++)
            {
                allOk &= ApplyZoneColorRaw(zone, r, g, b);
                Thread.Sleep(20);
            }
            return allOk;
        }

        private void ApplyLightingMode(int mode)
        {
            if (mode == 0)
            {
                // Re-assert each zone's own stored color (not a single
                // flattened color) - e.g. when brightness changes while in
                // static mode with different colors per zone.
                SendCommand("SetGamingLEDBehavior", 0x07ul);
                Thread.Sleep(50);
                for (int zone = 0; zone < 4; zone++)
                {
                    var (zr, zg, zb) = _zoneColors[zone];
                    ApplyZoneColorRaw(zone, zr, zg, zb);
                    Thread.Sleep(20);
                }
                return;
            }

            SendCommand("SetGamingLEDBehavior", 0x07ul);
            Thread.Sleep(50);

            if (mode != 2 && mode != 3)
            {
                ulong zonePayload = 0x06ul | (0x0Ful << 8)
                    | ((ulong)_lastR << 16) | ((ulong)_lastG << 24) | ((ulong)_lastB << 32);
                SendCommand("SetGamingLEDBehavior", zonePayload);
                Thread.Sleep(20);
            }

            byte[] payload = new byte[16];
            payload[0] = (byte)mode;     
            payload[1] = _speed;         
            payload[2] = _brightness;    
            payload[3] = _direction;     
            payload[5] = _lastR;
            payload[6] = _lastG;
            payload[7] = _lastB;
            payload[9] = 1;              
            SendLedCommand(payload);
        }

        public int CpuTemp => GetSensorReading(0x01);
        public int GpuTemp => GetSensorReading(0x0A);
        public int CpuFanRpm => GetSensorReading(0x02);
        public int GpuFanRpm => GetSensorReading(0x06);

        private void SyncWindowsPowerMode(byte acerMode)
        {
            try
            {
                Guid overlay = acerMode switch
                {
                    0x00 or 0x06 => OVERLAY_EFFICIENCY,
                    0x04 or 0x05 => OVERLAY_PERFORMANCE,
                    _ => OVERLAY_BALANCED
                };
                PowerSetActiveOverlayScheme(overlay);
            }
            catch { }
        }

        private ManagementObject? GetBatteryControlObject()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryControl");
                using var results = searcher.Get();
                return results.Cast<ManagementObject>().FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        public bool SetBatteryChargeLimit(bool enable)
        {
            try
            {
                using var obj = GetBatteryControlObject();
                if (obj == null) return false;

                using var inParams = obj.GetMethodParameters("SetBatteryHealthControl");
                inParams["uBatteryNo"] = (byte)1;
                inParams["uFunctionMask"] = (byte)1;
                inParams["uFunctionStatus"] = (byte)(enable ? 1 : 0);
                inParams["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };

                using var outParams = obj.InvokeMethod("SetBatteryHealthControl", inParams, null);
                ushort result = Convert.ToUInt16(outParams["uReturn"]);
                return result == 0;
            }
            catch
            {
                return false;
            }
        }
        public bool IsBatteryControlSupported()
        {
            try
            {
                using var obj = GetBatteryControlObject();
                return obj != null;
            }
            catch
            {
                return false;
            }
        }

        // Keyboard backlight auto-off (30s idle) lives on a completely different
        // WMI class than everything else in this file - APGeAction, not
        // AcerGamingFunction. Confirmed live via:
        //   Get-CimClass -Namespace root\wmi | ? { $_.CimClassQualifiers['guid'].Value -match '61EF69EA' }
        // which resolved to APGeAction. Action codes (0x88401 read, 0x88402/
        // 0x1E0000088402 write) come from the Linuwu-Sense Linux driver; the
        // getter's exact bit encoding didn't match that driver's docs on this
        // firmware, so state is tracked locally (BacklightTimeoutEnabled) rather
        // than trusted from a hardware read - the write side was independently
        // confirmed correct by observing the actual backlight behavior.
        private ManagementObject? _cachedApgeObj;
        private bool _backlightTimeoutEnabled;

        private ManagementObject? GetApgeObject()
        {
            lock (_lock)
            {
                if (_cachedApgeObj != null) return _cachedApgeObj;
                try
                {
                    using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM APGeAction");
                    using var results = searcher.Get();
                    _cachedApgeObj = results.Cast<ManagementObject>().FirstOrDefault();
                }
                catch { _cachedApgeObj = null; }
                return _cachedApgeObj;
            }
        }

        public bool SetBacklightTimeout(bool enabled)
        {
            try
            {
                var obj = GetApgeObject();
                if (obj == null) return false;

                using var inParams = obj.GetMethodParameters("SetFunction");
                inParams["uiInput"] = enabled ? (ulong)0x1E0000088402 : (ulong)0x88402;
                using var outParams = obj.InvokeMethod("SetFunction", inParams, null);
                uint result = Convert.ToUInt32(outParams["uiOutput"]);
                if (result == 0) _backlightTimeoutEnabled = enabled;
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public bool BacklightTimeoutEnabled => _backlightTimeoutEnabled;

        public void Dispose()
        {
            lock (_lock)
            {
                _cachedObj?.Dispose();
                _cachedObj = null;
                _cachedApgeObj?.Dispose();
                _cachedApgeObj = null;
            }
        }
    }
}