using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SQLitePCL;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Ultrasonics
{
    internal class MainVm : ObservableRecipient
    {
        SerialListener? _listener;
        DataStore _store;

        ConcurrentDictionary<int, (SingleUltProcessor, EdgeUltProcessor)> _processors = new();
        public ObservableCollection<WindVm> WindVms { get; } = new ObservableCollection<WindVm>();
        public ObservableCollection<WindVm> EdgeWindVms { get; } = new ObservableCollection<WindVm>();

        public Dispatcher? ThisDispatcher { get; set; }

        string[]? _availableSerialPorts;
        public string[]? AvaliableSerialPorts
        {
            get => _availableSerialPorts;
            set => SetProperty(ref _availableSerialPorts, value);
        }

        string? _selectedPort;
        public string? SelectedPort
        {
            get => _selectedPort;
            set
            {
                if (SetProperty(ref _selectedPort, value))
                    SetSerialPort();
            }
        }

        bool _calibrating;
        public bool Calibrating
        {
            get => _calibrating;
            set 
            {
                if (SetProperty(ref _calibrating, value))
                    OnCalibratingChanged();
            }
        }

        bool _displayPackets = true;

        bool _zoomCharts;
        public bool ZoomCharts
        {
            get => _zoomCharts;
            set
            {
                if (SetProperty(ref _zoomCharts, value))
                    OnZoomChartsChanged();
            }
        }

        bool _fftCharts;
        public bool FFTCharts
        {
            get => _fftCharts;
            set
            {
                if (SetProperty(ref _fftCharts, value))
                    OnZoomChartsChanged();
            }
        }

        public static double _averageTime = 5;
        public double AverageTime
        {
            get => _averageTime;
            set => SetProperty(ref _averageTime, value);
        }

        bool _recorded;
        public bool Recorded
        {
            get => _recorded;
            set
            {
                if (SetProperty(ref _recorded, value) && value)
                    DoDisconnect();
            }
        }

        bool _parseTimes = true;
        DateTimeOffset? _startTime, _endTime;
        string? _startTimeTxt, _endTimeTxt;
        public string? StartTimeTxt
        {
            get => _startTimeTxt;
            set
            {
                if (SetProperty(ref _startTimeTxt, value) && _parseTimes)
                {
                    if (DateTime.TryParse(value, out var tmp))
                        _startTime = tmp;
                    else
                        _startTime = null;
                    OnTimesChanged();

                }
            }
        }

        public string? EndTimeTxt
        {
            get => _endTimeTxt;
            set
            {
                if (SetProperty(ref _endTimeTxt, value) && _parseTimes)
                {
                    if (DateTime.TryParse(value, out var tmp))
                        _endTime = tmp;
                    else
                        _endTime = null;
                    OnTimesChanged();

                }
            }
        }

        double _selectedTimePortion;
        public double SelectedTimePortion
        {
            get => _selectedTimePortion;
            set
            {
                if (SetProperty(ref _selectedTimePortion, value))
                    OnTimesChanged();
            }
        }

        double _smallChange;
        public double SmallChange
        {
            get => _smallChange;
            set
            {
                if (SetProperty(ref _smallChange, value))
                    OnPropertyChanged(nameof(LargeChange));
            }
        }
        public double LargeChange => SmallChange * 10;

        DateTimeOffset? _currentTime;
        DateTimeOffset? CurrentTime
        {
            get => _currentTime;
            set
            {
                if (SetProperty(ref _currentTime, value))
                    OnPropertyChanged(nameof(CurrentTimeTxt));
            }
        }
        public string? CurrentTimeTxt => CurrentTime?.ToString("T");

        DateTimeOffset? _sessionStart;
        int _sessionStartRequested = 1;
        DateTimeOffset _lastSessionChaged;
        string? _currentSessionName;
        string? _nextSessionName;
        public string? SessionName
        {
            get => _nextSessionName;
            set
            {
                if (SetProperty(ref _nextSessionName, value))
                {
                    _sessionStartRequested = 1;
                    _lastSessionChaged = DateTimeOffset.Now;
                }
            }
        }

        string? _loadedSessionName;
        public string? LoadedSessionName
        {
            get => _loadedSessionName;
            set => SetProperty(ref _loadedSessionName, value);
        }

        bool _uiSuspended;
        void SuspendUI()
        {
            _uiSuspended = true;
        }
        void ResumeUI()
        {
            _uiSuspended = false;
            foreach (var vm in WindVms.Concat(EdgeWindVms))
                vm.UpdateUI(vm.Frame);
        }

        private async void OnTimesChanged()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            if (!Recorded)
                return;
            if (_startTime == null || _endTime == null
                || _endTime < _startTime)
            {
                CurrentTime = null;
                return;
            }
            var timeDiff = _endTime - _startTime;
            CurrentTime = _startTime + 
                timeDiff * SelectedTimePortion;
            SmallChange = 0.1 / timeDiff.Value.TotalSeconds;
            foreach (var vm in WindVms.Concat(EdgeWindVms))
                vm.ClearHistory();
            DateTimeOffset start = (CurrentTime - TimeSpan.FromSeconds(AverageTime)).Value;
            int packetCount = 0;
            SuspendUI();
            await foreach (var packet in _store.GetPackets(start, CurrentTime.Value))
            {
                HandlePacketInternal(packet);
                packetCount++;
            }
            double noUI = sw.ElapsedMilliseconds;
            ResumeUI();
            sw.Stop();
            GC.KeepAlive(noUI); 
            await DoRecordedWebData();
        }

        WebData? _cachedWebdata;
        private async Task DoRecordedWebData()
        {
            if (!Recorded)
                return;
            if (_startTime == null || _endTime == null
                || _endTime < _startTime || CurrentTime == null)
                return;
            if (_cachedWebdata == null ||
                _cachedWebdata.Start != _startTime ||
                _cachedWebdata.End != _endTime)
                _cachedWebdata = await WebData.Get(_startTime.Value, _endTime.Value);
            var packet = _cachedWebdata.GetAtTime(CurrentTime.Value);
            HandleWebPacket(packet);
        }

        private void OnZoomChartsChanged()
        {
            foreach (var vm in WindVms)
            {
                vm.ZoomCharts = ZoomCharts;
                vm.FFTCharts = FFTCharts;
            }
        }

        public RelayCommand RefreshCmd => new RelayCommand(RefreshSerialPorts);
        public RelayCommand ReconnectCmd => new RelayCommand(SetSerialPort);
        public RelayCommand DisconnectCmd => new RelayCommand(DoDisconnect);
        public RelayCommand ResetCmd => new RelayCommand(DoReset);
        public AsyncRelayCommand LoadCalibrationCmd => new AsyncRelayCommand(LoadCalibration);
        public AsyncRelayCommand LoadSessionCmd => new AsyncRelayCommand(LoadSession);
        public RelayCommand FinishSessionCmd => new RelayCommand(FinishSession);
        public RelayCommand GenerateReportCmd => new RelayCommand(GenerateReport);
        public RelayCommand LoadGpxCmd => new RelayCommand(LoadGpxData);

        GPXData? _gpxData;
        private void LoadGpxData()
        {
            OpenFileDialog ofd = new OpenFileDialog()
            {
                Filter = "GPX Files|*.gpx"
            };
            if (ofd.ShowDialog() != true)
                return;
            _gpxData = new GPXData(ofd.FileName);
        }

        private async void GenerateReport()
        {
            try
            {
                if (!Recorded)
                    return;
                if (_startTime == null || _endTime == null
                    || _endTime < _startTime)
                {
                    return;
                }
                StringBuilder output = new StringBuilder();
                output.Append(
                    "Time,GPS_Speed,SG_Speed,Combined_Speed,Short_Speed,Long_Speed,Cycle_Count,SG_Time,");
                for (int ch1 = 0; ch1 < 3; ch1++)
                    for (int ch2 = ch1 + 1; ch2 < 4; ch2++)
                    {
                        output.Append($"{ch1}{ch2}_Key_re,{ch1}{ch2}_Key_im,");
                        output.Append($"{ch2}{ch1}_Key_re,{ch2}{ch1}_Key_im,");
                    }
                output.AppendLine();
                WindFrameAction = frame =>
                {
                    var webData = _cachedWebdata?.GetAtTime(frame.Time);
                    output.Append(
                        $"{frame.Time}, {_gpxData?.GetSpeedAtTime(frame.Time):F1}," +
                        $"{webData?.windspeed / 3.6:F1}," +
                        $"{frame.Wind.Length()},{frame.ShortWind.Length()},{frame.LongWind.Length()}," +
                        $"{frame.Source.Values.First().Ch1.ExcitationCount}," +
                        $"{(webData == null ? "" : DateTimeOffset.FromUnixTimeSeconds(webData.timestamp).ToLocalTime())},");
                    for (int ch1 = 0; ch1 < 3; ch1++)
                        for (int ch2 = ch1 + 1; ch2 < 4; ch2++)
                        {
                            var ch1KeyVal = frame.Source[(ch1, ch2)].Ch1.KeyVal;
                            var ch2KeyVal = frame.Source[(ch1, ch2)].Ch2.KeyVal;
                            output.Append($"{ch1KeyVal.Real},{ch1KeyVal.Imaginary},");
                            output.Append($"{ch2KeyVal.Real},{ch2KeyVal.Imaginary},");
                        }
                    output.AppendLine();
                };
                SuspendUI();
                await foreach (var packet in _store.GetPackets(_startTime.Value, _endTime.Value))
                {
                    HandlePacketInternal(packet);
                }
                ResumeUI();
                WindFrameAction = null;
                SaveFileDialog sfd = new SaveFileDialog()
                {
                    FileName = $"{LoadedSessionName ?? "Ultrasonics"}.csv",
                    Filter = "CSV File|*.csv"
                };
                if (sfd.ShowDialog() != true)
                    return;
                File.WriteAllText(sfd.FileName, output.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        

        private void FinishSession()
        {
            CheckSession(true);
            SessionName = null;
            _sessionStartRequested = 0;
            _sessionStart = null;
        }

        private async Task LoadSession()
        {
            FinishSession();
            var vm = new SelectionVm<SessionInfo>
            {
                Items = await _store.GetSessions().ToListAsync()
            };
            var dlg = new CalibrationSelectorDlg();
            dlg.DataContext = vm;
            if (dlg.ShowDialog() != true)
                return;
            var selection = vm.SelectedItem;
            if (selection == null)
                return;
            Recorded = true;
            _parseTimes = false;
            StartTimeTxt = selection.Start.ToString("G");
            EndTimeTxt = selection.End.ToString("G");
            _startTime = selection.Start;
            _endTime = selection.End;
            _parseTimes = true;
            LoadedSessionName = selection.Name;
            OnTimesChanged();
        }

        private async Task LoadCalibration()
        {
            var vm = new SelectionVm<CalibrationInfo>
            {
                Items = await _store.GetCalibrations().ToListAsync()
            };
            var dlg = new CalibrationSelectorDlg();
            dlg.DataContext = vm;
            if (dlg.ShowDialog() != true)
                return;
            var selection = vm.SelectedItem;
            if (selection == null)
                return;
            var oldRecorded = Recorded;
            Recorded = true;
            Calibrating = false;
            _dontStoreCalibration = true;
            Calibrating = true;
            _displayPackets = false;
            int packetCount = 0;
            double minTemp = 100, maxTemp = 0, sumTemp = 0;
            await foreach (var pkt in _store.GetPackets(selection.Start, selection.End))
            {
                var temp = pkt.Temperature_x10 / 10d;
                if (temp < minTemp)
                    minTemp = temp;
                if (temp > maxTemp)
                    maxTemp = temp;
                sumTemp += temp;
                HandlePacketInternal(pkt);
                packetCount++;
            }
            var avgTemp = sumTemp / packetCount;
            MessageBox.Show($"Calibration Complete. {packetCount} packets. Temperature: {avgTemp:F1} ({minTemp:F1} - {maxTemp:F1})");
            _displayPackets = true;
            Calibrating = false;
            _dontStoreCalibration = true;
            Recorded = oldRecorded;
        }

        void DoDisconnect()
        {
            var localListener = _listener;
            if (localListener == null)
                return;
            localListener.PacketReceived -= _listener_PacketReceived;
            //localListener.Stopped -= _listener_Stopped;
            localListener.Stop();
            _listener = null;
        }

        private void DoReset()
        {
            _listener?.WriteStream((byte)'R');
        }

        bool _dontStoreCalibration;
        DateTimeOffset _calibrationStart;
        private void OnCalibratingChanged()
        {
            if (_calibrating)
                foreach (var (proc1, proc2) in _processors.Values)
                {
                    proc1.BeginCalibration();
                    proc2.BeginCalibration();
                    _calibrationStart = DateTimeOffset.Now;
                }
            else
            {
                foreach (var (proc1, proc2) in _processors.Values)
                {
                    proc1.EndCalibration();
                    proc2.EndCalibration();
                }
                if (!_dontStoreCalibration)
                    _store.StoreCalibration(_calibrationStart, DateTimeOffset.Now);
            }
        }

        public MainVm()
        {
            if (Utils.IsInDesignMode)
                return;
            OnceOff.GenerateArrays();
            RefreshSerialPorts();
            _store = new DataStore("Data.sqlite");
            _globalVm = new WindVm(0)
            {
                ShowCharts = true
            };
            _globalEdgeVm = new WindVm(0);
            WindVms.Add(_globalVm);
            EdgeWindVms.Add(_globalEdgeVm);
            _webSource = new WebSource();
            _webSource.PacketReceived += _webSource_PacketReceived;
        }

        private void _webSource_PacketReceived(object? sender, SierraGlidingPacket packet)
        {
            if (Recorded)
                return;
            HandleWebPacket(packet);
        }

        private void HandleWebPacket(SierraGlidingPacket? packet)
        {
            Vector2? vec;
            if (packet != null)
            {
                var x = Math.Sin(packet.wind_direction / 180 * Math.PI);
                var y = Math.Cos(packet.wind_direction / 180 * Math.PI);
                vec = new Vector2((float)x, (float)y);
                vec *= (float)(packet.windspeed / 3.6); // SG sends in km/h
            }
            else
                vec = null;
            foreach (var vm in WindVms.Concat(EdgeWindVms))
                vm.WebWind = vec;
        }

        WebSource _webSource;
        WindVm _globalVm;
        WindVm _globalEdgeVm;

        void RefreshSerialPorts()
        {
            AvaliableSerialPorts = SerialPort.GetPortNames().Append("").ToArray();
        }

        void SetSerialPort()
        {
            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener = null;
                }
                if (!string.IsNullOrWhiteSpace(SelectedPort))
                {
                    _listener = new SerialListener(SelectedPort);
                    _listener.Stopped += _listener_Stopped;
                    _listener.PacketReceived += _listener_PacketReceived;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Change Serial Port", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void _listener_Stopped(object? sender, EventArgs e)
        {
            if (_closed)
                return;
            var listener = sender as SerialListener;
            if (listener == null)
                return;
            listener.Stopped -= _listener_Stopped;
            listener.PacketReceived -= _listener_PacketReceived;
            MessageBox.Show("Disconnected.");
            SelectedPort = "";
        }

        void DoThreadsafe(Action action)
        {
            var localDispatcher = ThisDispatcher;
            if (localDispatcher != null && !localDispatcher.CheckAccess())
            {
                localDispatcher.Invoke(action);
            }
            else
                action();
        }

        private void _listener_PacketReceived(object? sender, SerialPacket packet)
        {
            if (_closed)
                return;
            _store.StorePacket(packet);
            lock (_lockObj) // for debug only
                CheckSession(false);
            _lastPacketTime = packet.Timestamp;
            if (Recorded)
                return;
            HandlePacketInternal(packet);
        }

        DateTimeOffset _lastPacketTime;
        void CheckSession(bool forceSave)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (!forceSave && now - _lastSessionChaged < TimeSpan.FromSeconds(1))
                return;
            int localssr = Interlocked.Exchange(ref _sessionStartRequested, 0);
            bool newSession = localssr != 0;
            if (_sessionStart != null && (forceSave || newSession))
                _store.StoreSession(_sessionStart.Value, _lastPacketTime, _currentSessionName);
            if (newSession)
            {
                _sessionStart = now;
                _currentSessionName = _nextSessionName;
            }
        }

        bool _separated = false;
        void HandlePacketInternal(SerialPacket packet)
        {
            byte key = 0;
            if (_separated)
                key = packet.ExcitationCount;
            DoThreadsafe(() =>
            {
                if (!_processors.ContainsKey(key))
                {
                    var processor1 = new SingleUltProcessor();

                    var processor2 = new EdgeUltProcessor();
                    if (Calibrating)
                    {
                        processor1.BeginCalibration();
                        processor2.BeginCalibration();
                    }
                    _processors[key] = (processor1, processor2);
                    var vm1 = new WindVm(key)
                    {
                        ShowCharts = true
                    };
                    WindVms.Add(vm1);
                    var vm2 = new WindVm(key);
                    EdgeWindVms.Add(vm2);
                    processor1.NewFrameAvailable += (sender, frame) =>
                    {
                        WindFrameAction?.Invoke(frame);
                        if (!_displayPackets)
                            return;
                        vm1.SetFrame(frame, _uiSuspended);
                        _globalVm.SetFrame(frame, _uiSuspended);
                    };
                    processor2.NewFrameAvailable += (sender, frame) =>
                    {
                        if (!_displayPackets)
                            return;
                        vm2.SetFrame(frame, _uiSuspended);
                        _globalEdgeVm.SetFrame(frame, _uiSuspended);
                    };
                }
            });
            var (proc1, proc2) = _processors[key];
            lock (_lockObj) // DEBUG only
                proc1.ProcessSerialPacket(packet);
            proc2.ProcessSerialPacket(packet);
        }

        Action<WindFrame>? WindFrameAction = null;

        volatile bool _closed;
        public void OnClosing()
        {
            _listener?.Stop();
            _closed = true;
            CheckSession(true);
        }

        private object _lockObj = new object();
    }
}
