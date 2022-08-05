﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Gma.System.MouseKeyHook;
using LeagueOfLegendsBoxer.Application.ApplicationControl;
using LeagueOfLegendsBoxer.Application.Client;
using LeagueOfLegendsBoxer.Application.Event;
using LeagueOfLegendsBoxer.Application.Game;
using LeagueOfLegendsBoxer.Application.LiveGame;
using LeagueOfLegendsBoxer.Application.Request;
using LeagueOfLegendsBoxer.Helpers;
using LeagueOfLegendsBoxer.Models;
using LeagueOfLegendsBoxer.Pages;
using LeagueOfLegendsBoxer.Resources;
using LeagueOfLegendsBoxer.ViewModels.Pages;
using LeagueOfLegendsBoxer.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Notice = LeagueOfLegendsBoxer.Models.Notice;
using Teammate = LeagueOfLegendsBoxer.Models.Teammate;

namespace LeagueOfLegendsBoxer.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        private readonly string _cmdPath = @"C:\Windows\System32\cmd.exe";
        private readonly string _excuteShell = "wmic PROCESS WHERE name='LeagueClientUx.exe' GET commandline";
        public AsyncRelayCommand LoadCommandAsync { get; set; }
        public RelayCommand ShiftSettingsPageCommand { get; set; }
        public RelayCommand ShiftMainPageCommand { get; set; }
        public RelayCommand ShiftNoticePageCommand { get; set; }
        public RelayCommand OpenChampionSelectToolCommand { get; set; }
        public AsyncRelayCommand ResetCommandAsync { get; set; }
        public RelayCommand ExitCommand { get; set; }

        private Page _currentPage;
        public Page CurrentPage
        {
            get => _currentPage;
            set => SetProperty(ref _currentPage, value);
        }

        private bool _connected;
        public bool Connected
        {
            get => _connected;
            set => SetProperty(ref _connected, value);
        }

        private string gameStatus;
        public string GameStatus
        {
            get => gameStatus;
            set => SetProperty(ref gameStatus, value);
        }

        private int unReadNotices;
        public int UnReadNotices
        {
            get => unReadNotices;
            set => SetProperty(ref unReadNotices, value);
        }

        private bool _isLoop = false;
        private bool _isLoopLive = false;
        private IKeyboardMouseEvents _keyboardMouseEvent;
        private Task _loopForGameEvent;
        private ManualResetEvent _resetEvent = new ManualResetEvent(true); //控制轮询实时游戏事件的暂停和复原
        public List<Account> Team1Accounts { get; set; } = new List<Account>();
        public List<Account> Team2Accounts { get; set; } = new List<Account>();
        private readonly IApplicationService _applicationService;
        private readonly IRequestService _requestService;
        private readonly IGameService _gameService;
        private readonly IClientService _clientService;
        private readonly IEventService _eventService;
        private readonly IniSettingsModel _iniSettingsModel;
        private readonly IConfiguration _configuration;
        private readonly Settings _settingsPage;
        private readonly MainPage _mainPage;
        private readonly LeagueOfLegendsBoxer.Pages.Notice _notice;
        private readonly ChampionSelectTool _championSelectTool;
        private readonly ILogger<MainWindowViewModel> _logger;
        private readonly ImageManager _imageManager;
        private readonly RuneViewModel _runeViewModel;
        private readonly ILiveGameService _livegameservice;
        private readonly TeammateViewModel _teammateViewModel;
        private readonly Team1V2Window _team1V2Window;

        public MainWindowViewModel(IApplicationService applicationService,
                                   IClientService clientService,
                                   IRequestService requestService,
                                   IEventService eventService,
                                   IGameService gameService,
                                   IniSettingsModel iniSettingsModel,
                                   IConfiguration configuration,
                                   Settings settingsPage,
                                   MainPage mainPage,
                                   ImageManager imageManager,
                                   RuneViewModel runeViewModel,
                                   ChampionSelectTool championSelectTool,
                                   ILogger<MainWindowViewModel> logger,
                                   ILiveGameService livegameservice,
                                   TeammateViewModel teammateViewModel,
                                   LeagueOfLegendsBoxer.Pages.Notice notice,
                                   Team1V2Window team1V2Window)
        {
            LoadCommandAsync = new AsyncRelayCommand(LoadAsync);
            ShiftSettingsPageCommand = new RelayCommand(OpenSettingsPage);
            ShiftMainPageCommand = new RelayCommand(OpenMainPage);
            OpenChampionSelectToolCommand = new RelayCommand(OpenChampionSelectTool);
            ResetCommandAsync = new AsyncRelayCommand(ResetAsync);
            ShiftNoticePageCommand = new RelayCommand(OpenNoticePage);
            ExitCommand = new RelayCommand(() => { Environment.Exit(0); });
            _applicationService = applicationService;
            _requestService = requestService;
            _clientService = clientService;
            _iniSettingsModel = iniSettingsModel;
            _configuration = configuration;
            _settingsPage = settingsPage;
            _notice = notice;
            _championSelectTool = championSelectTool;
            _mainPage = mainPage;
            _eventService = eventService;
            _gameService = gameService;
            _logger = logger;
            _runeViewModel = runeViewModel;
            _imageManager = imageManager;
            GameStatus = "获取状态中";
            _livegameservice = livegameservice;
            _teammateViewModel = teammateViewModel;
            _team1V2Window = team1V2Window;
            _keyboardMouseEvent = Hook.GlobalEvents();
            _keyboardMouseEvent.KeyDown += OnKeyDown;
            _keyboardMouseEvent.KeyUp += OnKeyUp;
            WeakReferenceMessenger.Default.Register<MainWindowViewModel, IEnumerable<Notice>>(this, (x, y) =>
            {
                UnReadNotices = y.Count();
            });
        }

        public static Keys StringToKeys(string keyStr)
        {
            if (string.IsNullOrWhiteSpace(keyStr))
                throw new ArgumentException("Cannot be null or whitespaces.", nameof(keyStr));
            Combination combination = Combination.FromString(keyStr);
            Keys result = combination.TriggerKey;
            foreach (var chord in combination.Chord)
            {
                result |= chord;
            }
            return result;
        }

        #region 热键
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == StringToKeys("Alt+Q") && (Team1Accounts.Count > 0 || Team2Accounts.Count > 0))
            {
                _team1V2Window.Opacity = 1;
                _team1V2Window.Topmost = true;
                e.Handled = true;
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyData == StringToKeys("Q+Alt")
                || e.KeyData == StringToKeys("Alt+Q")
                || e.KeyData == StringToKeys("Alt"))
            {
                _team1V2Window.Opacity = 0;
                e.Handled = true;
            }
        }

        #endregion 
        private async Task LoadAsync()
        {
            await LoadConfig();
            await (_notice.DataContext as NoticeViewModel).LoadAsync();
            await ConnnectAsync();
            Constant.Items = JsonConvert.DeserializeObject<IEnumerable<Item>>(await _gameService.GetItems());
            _eventService.Subscribe(Constant.ChampSelect, new EventHandler<EventArgument>(ChampSelect));
            _eventService.Subscribe(Constant.GameFlow, new EventHandler<EventArgument>(GameFlow));
            Connected = true;
            if (CurrentPage == _mainPage)
            {
                await (_mainPage.DataContext as MainViewModel).LoadAsync();
            }
            CurrentPage = _mainPage;
            GameStatus = "获取状态中";
            LoopLiveGameEventAsync();
            await LoopforClientStatus();
        }

        private async Task ResetAsync()
        {
            await LoadAsync();
        }

        private async Task LoopforClientStatus()
        {
            if (_isLoop)
                return;

            _isLoop = true;
            await Task.Yield();
            while (true)
            {
                try
                {
                    var data = await _clientService.GetZoomScaleAsync();
                    await Task.Delay(1500);
                }
                catch
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        Connected = false;
                        GameStatus = "断线中...";
                    });

                    await LoadAsync();
                    await Task.Delay(1500);
                }
            }
        }

        #region 打开各页面
        private void OpenSettingsPage()
        {
            CurrentPage = _settingsPage;
        }
        private void OpenMainPage()
        {
            CurrentPage = _mainPage;
        }
        private void OpenNoticePage() 
        {
            CurrentPage = _notice;
        }
        #endregion

        #region 各种事件
        private async void GameFlow(object obj, EventArgument @event)
        {
            var data = $"{@event.Data}";
            if (string.IsNullOrEmpty(data))
                return;

            if (data != "InProgress" && data != "GameStart")
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Team1Accounts.Clear();
                    Team2Accounts.Clear();
                    _team1V2Window.Hide();
                    _team1V2Window.Topmost = false;
                });
            }
            switch (data)
            {
                case "ReadyCheck":
                    _resetEvent.Reset();
                    GameStatus = "找到对局";
                    if (_iniSettingsModel.AutoAcceptGame)
                    {
                        await AutoAcceptAsync();
                    }
                    break;
                case "ChampSelect":
                    _resetEvent.Reset();
                    GameStatus = "英雄选择中";
                    await ChampSelectAsync();
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _championSelectTool.Show();
                        _championSelectTool.WindowStartupLocation = WindowStartupLocation.Manual;
                        _championSelectTool.Top = (SystemParameters.PrimaryScreenHeight - _championSelectTool.ActualHeight) / 2;
                        _championSelectTool.Left = SystemParameters.PrimaryScreenWidth - _championSelectTool.ActualWidth - 10;
                    });
                    break;
                case "None":
                    GameStatus = "大厅中或正在创建对局";
                    break;
                case "Reconnect":
                    GameStatus = "游戏中,等待重新连接";
                    break;
                case "Lobby":
                    GameStatus = "房间中";
                    break;
                case "Matchmaking":
                    GameStatus = "匹配中";
                    break;
                case "InProgress":
                    GameStatus = "游戏中"; 
                    break;
                case "GameStart":
                    GameStatus = "游戏开始了";
                    Team1Accounts = new List<Account>();
                    Team2Accounts = new List<Account>();
                    await ActionWhenGameBegin();
                    break;
                case "WaitingForStats":
                    GameStatus = "等待结算界面";
                    break;
                case "PreEndOfGame":
                case "EndOfGame":
                    GameStatus = "对局结束";
                    break;
                default:
                    GameStatus = "未知状态" + data;
                    break;
            }
        }
        private async void ChampSelect(object obj, EventArgument @event)
        {
            try
            {
                var gInfo = await _gameService.GetCurrentGameInfoAsync();
                var mode = JToken.Parse(gInfo)["gameData"]["queue"]["gameMode"].ToString();
                var myData = JObject.Parse(@event.Data.ToString());
                int playerCellId = int.Parse(@event.Data["localPlayerCellId"].ToString());
                IEnumerable<Team> teams = JsonConvert.DeserializeObject<IEnumerable<Team>>(@event.Data["myTeam"].ToString());
                var me = teams.FirstOrDefault(x => x.CellId == playerCellId);
                if (me == null)
                    return;

                if (mode == "ARAM")
                {
                    await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                    {
                        await _runeViewModel.LoadChampInfoAsync(me.ChampionId, true);
                    });

                    if (_iniSettingsModel.AutoLockHeroInAram)
                    {
                        int[] champs = JsonConvert.DeserializeObject<int[]>(@event.Data["benchChampionIds"].ToString());
                        var loc = _iniSettingsModel.LockHerosInAram.IndexOf(me.ChampionId);
                        loc = loc == -1 ? _iniSettingsModel.LockHerosInAram.Count : loc;
                        if (loc != 0)
                        {
                            var heros = _iniSettingsModel.LockHerosInAram.Take(loc);
                            var swapHeros = new List<int>();
                            foreach (var item in heros)
                            {
                                if (champs.Contains(item))
                                {
                                    swapHeros.Add(item);
                                }
                            }

                            for (var index = swapHeros.Count - 1; index >= 0; index--)
                            {
                                await _gameService.BenchSwapChampionsAsync(swapHeros[index]);
                            }
                        }
                    }
                }
                else
                {
                    foreach (var action in @event.Data["actions"])
                    {
                        foreach (var actionItem in action)
                        {
                            if (int.Parse(actionItem["actorCellId"].ToString()) == playerCellId)
                            {
                                if (actionItem["type"] == "pick")
                                {
                                    foreach (var teamPlayer in myData["myTeam"])
                                    {
                                        if (teamPlayer["cellId"] == playerCellId)
                                        {
                                            int champ = teamPlayer["championId"];
                                            if (int.Parse((string)actionItem["championId"]) != 0 && champ != 0)
                                            {
                                                await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                                                {
                                                    await _runeViewModel.LoadChampInfoAsync(champ, false);
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
        }

        private void LoopLiveGameEventAsync()
        {
            if (_isLoopLive)
                return;

            _isLoopLive = true;
            var _ = Task.Run(async () =>
            {
                while (true)
                {
                    var gInfo = await _gameService.GetCurrentGameInfoAsync();
                    if (JToken.Parse(gInfo)["phase"].ToString() == "InProgress")
                    {
                        if (Team1Accounts.Count <= 0 && Team2Accounts.Count <= 0)
                        {
                            await ActionWhenGameBegin();
                        }
                        else if (Team1Accounts.All(x => x.Champion == null) && Team2Accounts.All(x => x.Champion == null))
                        {
                            var teams1 = await _livegameservice.GetPlayersAsync(100);
                            var teams2 = await _livegameservice.GetPlayersAsync(200);
                            if (!string.IsNullOrEmpty(teams1) && !string.IsNullOrEmpty(teams2))
                            {
                                var token1 = JArray.Parse(teams1);
                                var token2 = JArray.Parse(teams2);

                                foreach (var item in token1)
                                {
                                    var name = item["summonerName"].ToObject<string>();
                                    var account = (Team1Accounts.Concat(Team2Accounts)).FirstOrDefault(x => x.DisplayName == name);
                                    var championName = item["championName"].ToObject<string>();
                                    account.Champion =  Constant.Heroes.FirstOrDefault(x => x.Label == championName);
                                }
                            }
                        }
                        else
                        {
                            await Task.Delay(30000);
                        }
                    }
                    else 
                    {
                        await Task.Delay(5000);
                    }
                }
            });
        }

        private async Task AutoAcceptAsync()
        {
            await _gameService.AutoAcceptGameAsync();
        }

        private async Task ChampSelectAsync()
        {
            await Task.Yield();
            var _ = Task.Run(async () =>
            {
                while (true)
                {
                    var session = await _gameService.GetGameSessionAsync();
                    var token = JToken.Parse(session);
                    if (token.Value<int>("httpStatus") != 404)
                    {
                        var localPlayerCellId = token.Value<int>("localPlayerCellId");
                        var actions = token.Value<IEnumerable<IEnumerable<JToken>>>("actions");
                        int userActionID;
                        foreach (var action in actions)
                        {
                            foreach (var actionElement in action)
                            {
                                if (actionElement.Value<int>("actorCellId") == localPlayerCellId && actionElement.Value<bool>("isInProgress"))
                                {
                                    userActionID = actionElement.Value<int>("id");
                                    if (actionElement.Value<string>("type") == "pick"
                                        && !actionElement.Value<bool>("completed")
                                        && _iniSettingsModel.AutoLockHero
                                        && _iniSettingsModel.AutoLockHeroChampId != default)
                                    {
                                        await _gameService.AutoLockHeroAsync(userActionID, _iniSettingsModel.AutoLockHeroChampId);
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    await Task.Delay(500);
                }
            });
        }

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
        private async Task ActionWhenGameBegin()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => _championSelectTool?.Hide());
            try
            {
                var gameInformation = await _gameService.GetCurrentGameInfoAsync();
                var token = JToken.Parse(gameInformation)["gameData"];
                var t1 = token["teamOne"].ToObject<IEnumerable<Teammate>>();
                var t2 = token["teamTwo"].ToObject<IEnumerable<Teammate>>();

                if (t1.All(x => x.SummonerId == default) && t2.All(x => x.SummonerId == default))
                {
                    return;
                }

                if (!t1.All(x => string.IsNullOrEmpty(x.Puuid?.Trim())))
                {
                    Team1Accounts = await TeamToAccountsAsync(t1);
                }

                if (!t2.All(x => string.IsNullOrEmpty(x.Puuid?.Trim())))
                {
                    Team2Accounts = await TeamToAccountsAsync(t2);
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    (_team1V2Window.DataContext as Team1V2WindowViewModel).LoadData(Team1Accounts, Team2Accounts);
                    _team1V2Window.Topmost = true;
                    _team1V2Window.Opacity = 0;
                    _team1V2Window.Show();
                    _team1V2Window.Activate();
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
        }

        public async Task<List<Account>> TeamToAccountsAsync(IEnumerable<Teammate> teammates)
        {
            var accounts = new List<Account>();
            var teamvm = App.ServiceProvider.GetRequiredService<TeammateViewModel>();
            int teamId = 1;
            List<(int, int)> teams = new List<(int, int)>();
            foreach (var id in teammates)
            {
                var account = await teamvm.GetAccountAsync(id.SummonerId);
                if (id.TeamParticipantId == null)
                {
                    account.TeamID = teamId++;
                }
                else
                {
                    var team = teams.FirstOrDefault(x => x.Item2 == id.TeamParticipantId);
                    if (team == default)
                    {
                        account.TeamID = teamId++;
                        teams.Add((account.TeamID, id.TeamParticipantId.Value));
                    }
                    else
                    {
                        account.TeamID = team.Item1;
                    }
                }

                if (!string.IsNullOrEmpty(id.GameCustomization?.Perks))
                {
                    account.Runes = new ObservableCollection<Rune>(JToken.Parse(id.GameCustomization?.Perks)["perkIds"].ToObject<IEnumerable<int>>()?.Select(x => Constant.Runes.FirstOrDefault(y => y.Id == x))?.ToList());
                }
                if (account != null)
                {
                    accounts.Add(account);
                }
            }

            return accounts;
        }
        #endregion

        private async Task<string> GetAuthenticate()
        {
            using (Process p = new Process())
            {
                p.StartInfo.FileName = _cmdPath;
                p.StartInfo.UseShellExecute = false; //是否使用操作系统shell启动
                p.StartInfo.RedirectStandardInput = true; //接受来自调用程序的输入信息
                p.StartInfo.RedirectStandardOutput = true; //由调用程序获取输出信息
                p.StartInfo.RedirectStandardError = true; //重定向标准错误输出
                p.StartInfo.CreateNoWindow = true; //不显示程序窗口
                p.Start();
                p.StandardInput.WriteLine(_excuteShell.TrimEnd('&') + "&exit");
                p.StandardInput.AutoFlush = true;
                string output = await p.StandardOutput.ReadToEndAsync();
                p.WaitForExit();
                p.Close();

                return output;
            }
        }

        private async Task ConnnectAsync()
        {
            while (true)
            {
                try
                {
                    var authenticate = await GetAuthenticate();
                    if (!string.IsNullOrEmpty(authenticate) && authenticate.Contains("--remoting-auth-token="))
                    {
                        var tokenResults = authenticate.Split("--remoting-auth-token=");
                        var portResults = authenticate.Split("--app-port=");
                        var PidResults = authenticate.Split("--app-pid=");
                        var installLocations = authenticate.Split("--install-directory=");
                        Constant.Token = tokenResults[1].Substring(0, tokenResults[1].IndexOf("\""));
                        Constant.Port = int.TryParse(portResults[1].Substring(0, portResults[1].IndexOf("\"")), out var temp) ? temp : 0;
                        Constant.Pid = int.TryParse(PidResults[1].Substring(0, PidResults[1].IndexOf("\"")), out var temp1) ? temp1 : 0;
                        if (string.IsNullOrEmpty(Constant.Token) || Constant.Port == 0)
                            throw new InvalidOperationException("invalid data when try to crack.");

                        var settingFileLocation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configuration.GetSection("SettingsFileLocation").Value);
                        await Task.WhenAll(_requestService.Initialize(Constant.Port, Constant.Token),
                                           _eventService.Initialize(Constant.Port, Constant.Token));

                        await _eventService.ConnectAsync();
                        break;
                    }
                    else
                        throw new InvalidOperationException("can't read right token and port");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                    await Task.Delay(5000);
                }
            }
        }

        private async Task LoadConfig()
        {
            //获取所有天赋列表
            var runes = await _requestService.GetJsonResponseAsync(HttpMethod.Get, "https://game.gtimg.cn/images/lol/act/img/js/runeList/rune_list.js");
            var runeDic = JToken.Parse(runes)["rune"].ToObject<IDictionary<int, Rune>>();
            foreach (var runed in runeDic)
            {
                runed.Value.Id = runed.Key;
            }
            Constant.Runes = runeDic.Select(x => x.Value).ToList();
            var heros = await _requestService.GetJsonResponseAsync(HttpMethod.Get, "https://game.gtimg.cn/images/lol/act/img/js/heroList/hero_list.js");
            Constant.Heroes = JToken.Parse(heros)["hero"].ToObject<IEnumerable<Hero>>();

            await _iniSettingsModel.Initialize();
        }

        private void OpenChampionSelectTool()
        {
            _championSelectTool.Show();
            _championSelectTool.WindowStartupLocation = WindowStartupLocation.Manual;
            _championSelectTool.Top = (SystemParameters.PrimaryScreenHeight - _championSelectTool.ActualHeight) / 2;
            _championSelectTool.Left = SystemParameters.PrimaryScreenWidth - _championSelectTool.ActualWidth - 10;
            _championSelectTool.Topmost = true;
            var _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _championSelectTool.Topmost = false;
                });
            });
        }
    }
}
