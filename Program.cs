using System.Runtime.InteropServices;
using System.Text.Json;

using HidSharp;
using vJoyInterfaceWrap;
using SharpDX.DirectInput;
namespace TH8ATranslatorFS25AxesCal
{

    enum Th8Mode
    {
        Unknown,
        HPattern,
        Sequential
    }


    class Th8State
    {
        public Th8Mode Mode { get; init; }
        public int GearNumber { get; init; } // 1..7, 0 = нет передачи
        public bool IsReverse { get; init; } // R
        public bool IsNeutral { get; init; }
        public bool SeqUp { get; init; }
        public bool SeqDown { get; init; }
    }


    class Th8Reader : IDisposable
    {
        private readonly DirectInput _di;
        private Joystick? _joystick;

        // Индексы кнопок — это предположение:
        // 0..6 = передачи 1..7, 7 = R, 8 = Seq+, 9 = Seq-
        private const int GearButtonsCount = 8;
        private const int SeqUpIndex = 9;
        private const int SeqDownIndex = 8;

        public Th8Reader()
        {
            _di = new DirectInput();
            InitializeJoystick();
        }
        
        private void InitializeJoystick()
        {
            // Ищем устройство по имени (можете подправить фильтр)
            var devices = _di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);

            var devInfo = devices.FirstOrDefault(d =>
                (d.InstanceName?.Contains("Gear Shift", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (d.ProductName?.Contains("Gear Shift", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (d.InstanceName?.Contains("TH8", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (d.ProductName?.Contains("TH8", StringComparison.OrdinalIgnoreCase) ?? false));

            if (devInfo == null || devInfo.InstanceGuid == Guid.Empty)
            {
                throw new Exception("TH8A (T500 RS Gear Shift) не найден через DirectInput.");
            }

            _joystick = new Joystick(_di, devInfo.InstanceGuid);
            _joystick.Properties.BufferSize = 32;
            _joystick.Acquire();

            Console.WriteLine($"[TH8] Подключено устройство: '{devInfo.InstanceName}' / '{devInfo.ProductName}'");
        }

        public Th8State ReadState()
        {
            if (_joystick == null)
            {
                return new Th8State { Mode = Th8Mode.Unknown, IsNeutral = true };
            }

            try
            {
                _joystick.Poll();
            }
            catch
            {
                // Попробуем пере-активировать
                try
                {
                    _joystick.Acquire();
                    _joystick.Poll();
                }
                catch
                {
                    return new Th8State { Mode = Th8Mode.Unknown, IsNeutral = true };
                }
            }

            var state = _joystick.GetCurrentState();
            bool[] buttons = state.Buttons ?? Array.Empty<bool>();

            // 1) Определяем передачу (H-режим)
            int gearNumber = 0;
            bool isReverse = false;

            for (int i = 0; i < GearButtonsCount && i < buttons.Length; i++)
            {
                if (buttons[i])
                {
                    gearNumber = i + 1; // 1..8
                    isReverse = (i == GearButtonsCount - 1); // индекс 7 = R
                    break;
                }
            }

            bool anyGear = gearNumber > 0;
            bool isNeutral = !anyGear;

            // 2) Определяем seq входы
            bool seqUp = buttons.Length > SeqUpIndex && buttons[SeqUpIndex];
            bool seqDown = buttons.Length > SeqDownIndex && buttons[SeqDownIndex];

            Th8Mode mode;
            if (anyGear)
            {
                mode = Th8Mode.HPattern;
            }
            else if (seqUp || seqDown)
            {
                mode = Th8Mode.Sequential;
            }
            else
            {
                mode = Th8Mode.Unknown;
            }

            // В H-режиме считаем 8-ю передачу как Reverse
            int gearNumNoR = 0;
            if (anyGear && !isReverse)
            {
                gearNumNoR = gearNumber; // 1..7
            }

            return new Th8State
            {
                Mode = mode,
                GearNumber = gearNumNoR,
                IsReverse = isReverse,
                IsNeutral = isNeutral,
                SeqUp = seqUp,
                SeqDown = seqDown
            };
        }

        public void Dispose()
        {
            _joystick?.Unacquire();
            _joystick?.Dispose();
            _di.Dispose();
        }
    }



    class VJoyAxisController : IDisposable
    {
        private readonly vJoy _vjoy;
        private readonly uint _id;
        private readonly int _axisMin;
        private readonly int _axisMax;
        private readonly int _axisCenter;
        private readonly Dictionary<int, int> _gearToAxis; // 1..7, 8=R
        private int _lastAxisValue;

        public VJoyAxisController(uint id)
        {
            _id = id;
            _vjoy = new vJoy();

            if (!_vjoy.vJoyEnabled())
            {
                throw new Exception("Драйвер vJoy не включен. Установите vJoy и перезагрузите систему.");
            }

            var status = _vjoy.GetVJDStatus(_id);
            if (status != VjdStat.VJD_STAT_FREE && status != VjdStat.VJD_STAT_OWN)
            {
                throw new Exception($"vJoy устройство {_id} сейчас занято или недоступно (статус: {status}).");
            }

            if (!_vjoy.AcquireVJD(_id))
            {
                throw new Exception($"Не удалось захватить vJoy устройство ID={_id}.");
            }

            long minRaw = 0, maxRaw = 0;
            if (!_vjoy.GetVJDAxisMin(_id, HID_USAGES.HID_USAGE_X, ref minRaw) ||
                !_vjoy.GetVJDAxisMax(_id, HID_USAGES.HID_USAGE_X, ref maxRaw))
            {
                throw new Exception("Ось X на vJoy-устройстве не доступна. Проверьте конфигурацию vJoy.");
            }

            // Преобразуем в int для внутреннего использования
            _axisMin = (int)minRaw;
            _axisMax = (int)maxRaw;
            _axisCenter = (_axisMin + _axisMax) / 2;
            _lastAxisValue = _axisCenter;

            // Строим карту значений оси для передач 1..7 и R(=8)
            _gearToAxis = BuildGearAxisMap();

            // Ставим ось в нейтраль
            SetAxis(_axisCenter);

            Console.WriteLine($"[vJoy] Устройство {_id}: Axis X [{_axisMin}..{_axisMax}], нейтраль = {_axisCenter}");
        }

        private Dictionary<int, int> BuildGearAxisMap()
        {
            var map = new Dictionary<int, int>();

            // Логика: R = max, 1 = min, 2..7 равномерно между
            // Всего 7 передач (1..7) + R (8)
            int gearsWithoutR = 7;
            for (int gear = 1; gear <= gearsWithoutR; gear++)
            {
                // линейное распределение на [min..max*0.9] например
                double t = (gear - 1) / (double)(gearsWithoutR - 1); // 0..1
                int value = _axisMin + (int)((_axisMax - _axisMin) * 0.9 * t);
                map[gear] = value;
            }

            // R (8) = максимум оси
            map[8] = _axisMax;

            Console.WriteLine("[vJoy] Таблица осевых значений для передач (H-режим):");
            foreach (var kvp in map.OrderBy(k => k.Key))
            {
                string label = kvp.Key == 8 ? "R" : kvp.Key.ToString();
                Console.WriteLine($"  Gear {label} => Axis X = {kvp.Value}");
            }

            return map;
        }

        private void SetAxis(int value)
        {
            if (value == _lastAxisValue) return;
            _vjoy.SetAxis(value, _id, HID_USAGES.HID_USAGE_X);
            _lastAxisValue = value;
        }

        public void SetNeutral()
        {
            SetAxis(_axisCenter);
        }

        /// <summary>
        /// Установить ось в зависимости от передачи (H-режим).
        /// gear: 1..7, 8=R, null = нейтраль.
        /// </summary>
        public void SetGear(int? gear)
        {
            if (gear == null)
            {
                SetAxis(_axisCenter);
                return;
            }

            int g = gear.Value;
            if (!_gearToAxis.TryGetValue(g, out int axisVal))
            {
                axisVal = _axisCenter;
            }
            SetAxis(axisVal);
        }

        /// <summary>
        /// Секвентальный режим: Up => максимум, Down => минимум, нейтраль => центр.
        /// </summary>
        public void SetSequential(bool up, bool down)
        {
            if (up && !down)
            {
                SetAxis(_axisMax);
            }
            else if (down && !up)
            {
                SetAxis(_axisMin);
            }
            else
            {
                // Оба отпущены или конфликт — центр
                SetAxis(_axisCenter);
            }
        }

        public void Dispose()
        {
            try
            {
                SetAxis(_axisCenter);
            }
            catch
            {
                // игнор
            }

            _vjoy.RelinquishVJD(_id);
        }
    }




    static class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("TH8A → vJoy ось X (H-pattern + Sequential). Нажмите ESC для выхода.\n");

            try
            {
                using var th8 = new Th8Reader();
                using var vj = new VJoyAxisController(1);

                Th8Mode lastMode = Th8Mode.Unknown;
                int lastGearLogical = -1; // 1..7, 8=R, 0=нейтраль
                bool lastSeqUp = false, lastSeqDown = false;

                while (true)
                {
                    // Выход по ESC
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Escape)
                            break;
                    }

                    Th8State s = th8.ReadState();

                    var mode = s.Mode;
                    if (mode == Th8Mode.Unknown && lastMode != Th8Mode.Unknown)
                    {
                        mode = lastMode;
                    }

                    // Определяем H/Sequential
                    if (s.Mode == Th8Mode.HPattern)
                    {
                        int logicalGear;

                        if (s.IsNeutral)
                        {
                            logicalGear = 0;
                        }
                        else if (s.IsReverse)
                        {
                            logicalGear = 8; // R
                        }
                        else
                        {
                            logicalGear = s.GearNumber; // 1..7
                        }

                        if (logicalGear != lastGearLogical || mode != lastMode)
                        {
                            vj.SetGear(logicalGear == 0 ? (int?)null : logicalGear);

                            string label = logicalGear == 0
                                ? "N"
                                : logicalGear == 8 ? "R" : logicalGear.ToString();

                            Console.WriteLine($"[H] Передача => {label}");
                            lastGearLogical = logicalGear;
                        }

                        lastSeqUp = lastSeqDown = false;
                    }
                    else if (s.Mode == Th8Mode.Sequential)
                    {
                        bool up = s.SeqUp;
                        bool down = s.SeqDown;

                        if (up != lastSeqUp || down != lastSeqDown || mode != lastMode)
                        {
                            vj.SetSequential(up, down);
                            string label = up ? "+" : down ? "-" : "N";
                            Console.WriteLine($"[SEQ] Состояние => {label}");
                            lastSeqUp = up;
                            lastSeqDown = down;
                        }

                        lastGearLogical = -1;
                    }
                    else
                    {
                        // Неопределённый режим / всё отпущено — нейтраль
                        if (lastMode != Th8Mode.Unknown)
                        {
                            vj.SetNeutral();
                            Console.WriteLine("[?] Неопределённый режим / нейтраль.");
                            lastGearLogical = -1;
                            lastSeqUp = lastSeqDown = false;
                        }
                    }

                    Thread.Sleep(5);
                    lastMode = mode;
                }

                Console.WriteLine("Выход. Ось возвращена в нейтраль.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Ошибка: " + ex.Message);
                Console.ResetColor();
                Console.WriteLine("Нажмите любую клавишу для выхода...");
                Console.ReadKey(true);
            }
        }
    }
}





