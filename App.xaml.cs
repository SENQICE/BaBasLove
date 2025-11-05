using System.Timers;
using System.Threading.Tasks;
#if ANDROID
using Android.Views;
using Android.OS;
using Android.Runtime;
#endif

namespace 爸爸的爱;

public partial class App : Application
{
    private System.Timers.Timer _usageTimer;
    private int _usageSeconds = 0;
    private const int LimitSeconds = 600; // 10分钟
    private bool _needQuiz = false; // 标志：是否需要弹出答题界面
    private bool _isNavigatingToQuiz = false; // 防重复导航
    private DateTime _lastQuizShown = DateTime.MinValue; // 新增：节流，防止短时间二次弹窗

    public App()
    {
        InitializeComponent();
        HookGlobalExceptionHandlers();

        MainPage = new NavigationPage(new MainPage(StartUsageTimer, StopUsageTimer));
        StartUsageTimer();
#if ANDROID
        SetStatusBarBlackText();
#endif
        TryShowLastCrash();
    }

    private void HookGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { LogCrash("UnhandledException", e.ExceptionObject as Exception); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try { LogCrash("UnobservedTaskException", e.Exception); } catch { }
        };
#if ANDROID
        AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
        {
            try { LogCrash("AndroidUnhandled", e.Exception); } catch { }
        };
#endif
    }

    private void TryShowLastCrash()
    {
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
            if (File.Exists(path))
            {
                var txt = File.ReadAllText(path);
                File.Delete(path);
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Application.Current?.MainPage?.DisplayAlert("上次崩溃信息", txt.Length > 800 ? txt[..800] + "..." : txt, "确定");
                });
            }
        }
        catch { }
    }

    private void LogCrash(string tag, Exception? ex)
    {
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}: {ex}\n{ex?.StackTrace}\n";
            File.AppendAllText(path, msg);
        }
        catch { }
    }

#if ANDROID
    // 提供统一方法，所有页面都可调用：避免调用 InsetsController造成 NPE
    public static void SetStatusBarBlackText()
    {
        try
        {
            var activity = Platform.CurrentActivity;
            if (activity == null) return;
            var window = activity.Window;
            if (window == null) return;

            // 设置底色为白
            window.SetStatusBarColor(Android.Graphics.Color.White);

            //仅在 API>=23 设置浅色状态栏文字（黑字）
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M && window.DecorView != null)
            {
                var flags = (int)window.DecorView.SystemUiVisibility;
                flags |= (int)SystemUiFlags.LightStatusBar;
                window.DecorView.SystemUiVisibility = (StatusBarVisibility)flags;
            }
            // API <23 不支持，保持系统默认
        }
        catch (Exception ex)
        {
            try
            {
                var path = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
                File.AppendAllText(path, $"[StatusBar-Black] {ex}\n");
            }
            catch { }
        }
    }

    // 状态栏透明并清除浅色文字标志（恢复系统默认/内容决定）
    public static void SetStatusBarTransparent()
    {
        try
        {
            var activity = Platform.CurrentActivity;
            if (activity == null) return;
            var window = activity.Window;
            if (window == null) return;

            window.SetStatusBarColor(Android.Graphics.Color.Transparent);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M && window.DecorView != null)
            {
                var flags = (int)window.DecorView.SystemUiVisibility;
                flags &= ~(int)SystemUiFlags.LightStatusBar; // 清除浅色文字标志
                window.DecorView.SystemUiVisibility = (StatusBarVisibility)flags;
            }
        }
        catch (Exception ex)
        {
            try
            {
                var path = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
                File.AppendAllText(path, $"[StatusBar-Transparent] {ex}\n");
            }
            catch { }
        }
    }
#endif

    protected override void OnStart()
    {
        base.OnStart();
        CheckAndShowQuiz();
    }

    protected override void OnResume()
    {
        base.OnResume();
        CheckAndShowQuiz();
    }

    private void CheckAndShowQuiz()
    {
        if (!_needQuiz) return;
        if (_isNavigatingToQuiz) return;
        //2 秒节流，避免重复弹出
        if ((DateTime.Now - _lastQuizShown).TotalSeconds < 2) return;
        var nav = Application.Current?.MainPage?.Navigation;
        if (nav?.ModalStack?.Any(p => p is QuizPage) == true) return; // 已在测验页

        _isNavigatingToQuiz = true;
        _needQuiz = false; // 消费标志
        _lastQuizShown = DateTime.Now;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Application.Current?.MainPage?.Navigation != null)
                {
                    await Application.Current.MainPage.Navigation.PushModalAsync(new QuizPage(OnQuizRoundFinished));
                }
            }
            catch { _needQuiz = true; }
            finally { _isNavigatingToQuiz = false; }
        });
    }

    private void StartUsageTimer()
    {
        if (_usageTimer != null)
        {
            _usageTimer.Stop();
            _usageTimer.Dispose();
        }
        _usageTimer = new System.Timers.Timer(1000);
        _usageTimer.Elapsed += (s, e) =>
        {
            _usageSeconds++;
            if (_usageSeconds >= LimitSeconds)
            {
                _needQuiz = true;
                MainThread.BeginInvokeOnMainThread(CheckAndShowQuiz); //统一入口
                _usageTimer.Stop();
            }
        };
        _usageTimer.Start();
    }

    private void StopUsageTimer()
    {
        _usageTimer?.Stop();
    }

    private void OnQuizRoundFinished(bool isSuccess, int correctCount, int totalCount)
    {
        //退出试卷后，先停表，避免误触发
        StopUsageTimer();

        //只有闯关成功才重置计时并重启
        if (isSuccess)
        {
            _usageSeconds = 0;
            _needQuiz = false;
            StartUsageTimer();
        }
        else
        {
            //失败或退出：记录已消费，不立刻重开，等待再次达到时长阈值
            _needQuiz = false;
        }
    }
}