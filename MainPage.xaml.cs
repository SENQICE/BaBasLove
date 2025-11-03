using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using System.IO;
using System.Text.Json;
#if ANDROID
using Android.Views;
#endif

namespace 爸爸的爱;

public partial class MainPage : ContentPage
{
    private readonly Action _startTimer;
    private readonly Action _stopTimer;
    private List<QuizRecord> _quizRecords = new();
    private List<QuizHistory> _quizHistories = new();
    private static readonly Random _rand = new();

    //主题状态
    private const string Pref_EyeCare = "EyeCareEnabled";

    // 成功/失败语句库（包含魔兽世界经典台词）
    private static readonly string[] SuccessMessages = new[]
    {
        "闯关完成，欢迎回来！",
        "干得漂亮！继续保持！",
        "太棒了，继续冲锋！",
        "大地母亲守护着你！",
        "愿风指引你的道路，勇士！",
        "为了艾泽拉斯！",
        "荣耀属于你！",
        "胜利属于勇敢者！",
        "继续高歌猛进！",
        "你就是今天的主角！",
        // 扩充
        "愿圣光与你同在！",
        "艾露恩保佑你！",
        "荣耀与力量！",
        "为了联盟！",
        "为了部落！",
        "下一关也拿下！",
        "胜利或死亡！（当然是胜利）",
        "为了联盟！",
        "为了部落！",
        "力量与智慧同在！",
        "继续前进，英雄！",
        "你的努力正在发光！",
        "今日无惧，明日无敌！",
        "前路畅通无阻！",
        "强者从不止步！"
    };

    private static readonly string[] FailureMessages = new[]
    {
        "闯关失败~再接再厉！",
        "不要气馁，再试一次！",
        "失败乃成功之母！",
        "你这是自寻死路！",
        "炎魔之王得到火焰会净化一切！",
        "你还没准备好！",
        "勇士，重新整顿再来！",
        "再努力一点点就成功了！",
        "坚持住，胜利就在前方！",
        // 扩充
        "现在还不是时候……",
        "重整旗鼓再战！",
        "别灰心，下一次更好！",
        "再来一次，这次会更好！",
        "失败不可怕，放弃才可怕！",
        "勇士，回去训练一下吧！",
        "风暴将至，迎难而上！",
        "失败只是起点，不是终点！",
        "每一次尝试都很重要！",
        "再来一次，这次会更好！",
        "从失败中学习就是胜利的一半！",
        "休整一下，立刻反击！",
        "系统关闭..闭...闭....",
        "为了..奎尔...萨拉斯...",
        "我还不能..."
    };

    public class QuizRecord
    {
        public int Attempt { get; set; }
        public int CorrectCount { get; set; }
        public int TotalCount { get; set; }
        public bool IsSuccess { get; set; }
    }

    public class QuizHistory
    {
        public DateTime Date { get; set; }
        public List<string> Meanings { get; set; } = new();
        public List<string> Answers { get; set; } = new();
        public List<string> UserAnswers { get; set; } = new();
        public int CorrectCount { get; set; }
        public int TotalCount { get; set; }
        public bool IsSuccess { get; set; }
    }

    // 心灵鸡汤（已替换）
    private static readonly string[] Soups = new[]
    {
        // 关于学习与探索
        "每天学一点，你就比昨天的自己更厉害！",
        "好奇心是通往知识宝藏的第一把钥匙。",
        "读一本书，就像交了一个新朋友。",
        "问题没有“笨”的，每一个提问都让智慧发光。",
        "错误是帮助我们进步的“小老师”。",
        "你的努力，正在为未来的惊喜铺路。",
        "知识就像超能力，学习就是你的训练场。",
        "试试看，说不定你会给自己一个惊喜！",
        "学习是给自己的最好礼物。",
        "每一次尝试，无论结果，你都赢了“不敢尝试”的自己。",

        // 关于成长与勇气
        "跌倒了没关系，站起来的样子最帅/最美！",
        "勇敢不是不害怕，而是害怕也愿意去试试。",
        "你比自己想象中要勇敢和强大。",
        "慢慢来，进步的小脚印最踏实。",
        "困难像弹簧，你强它就弱，你弱它就强。",
        "今天的“我不会”，就是明天的“我学会”！",
        "成长就是不断发现自己新的可能。",
        "别怕独自发光，你的光芒很特别。",
        "坚持下去，美好的事情正在发生。",
        "你的潜力无限大，等着你去发现。",

        // 关于自信与自我
        "你是独一无二的，这世界只有一个你！",
        "相信自己，你远比你认为的更出色。",
        "你的想法很宝贵，请大胆说出来。",
        "不用和别人比，你的对手是昨天的自己。",
        "你的笑容，能照亮整个教室。",
        "善待自己，像善待你最好的朋友一样。",
        "你的存在，本身就是一件美好的事。",
        "喜欢自己，是送给世界最好的礼物。",
        "你的优点，像星星一样多。",
        "做真实的自己，就是最棒的。",

        // 关于善良与朋友
        "一句温暖的鼓励，能点亮别人的一天。",
        "善良是一种超级力量，你拥有它。",
        "分享快乐，快乐就会变成双倍。",
        "好朋友是生命里的阳光，记得也要成为别人的阳光。",
        "帮助别人，温暖的是自己的心。",
        "谢谢和请，是世界上最动听的语言。",
        "你的一个善意的举动，可能会改变很多。",
        "团结合作，我们能创造奇迹。",
        "宽容别人，也是解放自己。",
        "友谊就像小树苗，需要用心浇灌。",

        // 综合与梦想
        "心怀梦想，未来可期。",
        "每一个“大人物”，都曾是一个“小学生”。",
        "保持你的善良和好奇心，它们会带你走很远。",
        "你的未来，由每一个今天的你创造。",
        "保持微笑，好运正在路上。",
        "你是爸爸妈妈/老师心中最亮的星。",
        "今天又是崭新的一天，充满新的希望！",
        "你的能量，超乎你的想象！",
        "做最棒的自己，就是最大的成功。",
        "记住，你永远被爱着，也被期待着。"
    };

    public MainPage(Action startTimer, Action stopTimer)
    {
        InitializeComponent();
        _startTimer = startTimer;
        _stopTimer = stopTimer;
        LoadQuizRecords();
        LoadQuizHistories();
        UpdateQuizRecordsView();

        // 启动随机一条鸡汤
        if (Soups.Length >0)
            SoupLabel.Text = Soups[_rand.Next(Soups.Length)];

        // 应用上次护眼模式
        bool eye = Preferences.Get(Pref_EyeCare, false);
        ApplyTheme(eye);
    }

    private void ApplyTheme(bool eyeCare)
    {
        // 普通模式（浅色）
        var bg = Color.FromArgb("#FFFFFF");
        var text1 = Color.FromArgb("#2C3E50");
        var text2 = Color.FromArgb("#4A6FA5");
        var btnBg = Color.FromArgb("#1976D2");
        var btnText = Colors.White;
        var frameBg = Color.FromArgb("#F5F7FB");
        var border = Color.FromArgb("#B3CDE0");
        var keyBg = Color.FromArgb("#B0B0B0");
        var keyText = Colors.Black;

        if (eyeCare)
        {
            // 蓝色系护眼（低饱和、低对比）
            bg = Color.FromArgb("#E3F2FD"); // 背景淡蓝
            text1 = Color.FromArgb("#2C3E50"); // 深蓝灰
            text2 = Color.FromArgb("#4A6FA5"); // 次级蓝灰
            btnBg = Color.FromArgb("#90CAF9"); // 柔蓝按钮
            btnText = Color.FromArgb("#2C3E50");
            frameBg = Color.FromArgb("#F0F4FF");
            border = Color.FromArgb("#B3CDE0");
            keyBg = Color.FromArgb("#B0C6DE");
            keyText = Color.FromArgb("#1F2A44");
        }

        Application.Current.Resources["AppColorPageBackground"] = bg;
        Application.Current.Resources["AppColorTextPrimary"] = text1;
        Application.Current.Resources["AppColorTextSecondary"] = text2;
        Application.Current.Resources["AppColorButtonBackground"] = btnBg;
        Application.Current.Resources["AppColorButtonText"] = btnText;
        Application.Current.Resources["AppColorFrameBackground"] = frameBg;
        Application.Current.Resources["AppColorBorder"] = border;
        Application.Current.Resources["AppColorKeyBackground"] = keyBg;
        Application.Current.Resources["AppColorKeyText"] = keyText;
    }

    private void OnTitleTapped(object sender, EventArgs e)
    {
        bool eye = Preferences.Get(Pref_EyeCare, false);
        eye = !eye;
        Preferences.Set(Pref_EyeCare, eye);
        ApplyTheme(eye);
        DisplayToast("已切换为" + (eye ? "护眼模式" : "普通模式"));
    }

    private async void DisplayToast(string msg)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Application.Current?.MainPage?.DisplayAlert("提示", msg, "确定");
            });
        }
        catch { }
    }

    private void OnQuizRecordsCleared()
    {
        _quizRecords.Clear();
        SaveQuizRecords();
        UpdateQuizRecordsView();
    }
    private async void OnParentModeClicked(object sender, EventArgs e)
    {
        // 正确写法：
        string pwd = await DisplayPromptAsync(
            "家长模式",
            "请输入家长密码",
            "确定",
            "取消",
            "",
            -1,
            Keyboard.Text
        );
        if (pwd == null)
        {
            // 取消直接返回首页
            return;
        }
        string realPwd = Preferences.Get("ParentPassword", "z123456");
        if (pwd == realPwd)
        {
            _stopTimer();
            WelcomeLabel.Text = "欢迎使用爸爸的爱";
            CountdownLabel.Text = "";
            SonModeButton.IsEnabled = true;
            await DisplayAlert("提示", "已进入家长模式，限制已解除。", "确定");
            await Navigation.PushModalAsync(new SettingsPage(OnQuizRecordsCleared));
        }
        else
        {
            await DisplayAlert("错误", "密码错误", "确定");
        }
    }

    private void OnSonModeClicked(object sender, EventArgs e)
    {
        SonModeButton.IsEnabled = false;
        WelcomeLabel.Text = "你已经进入儿子模式！";
        Application.Current.MainPage.Navigation.PushModalAsync(new QuizPage(OnQuizRoundFinished));
    }

    private async void OnReviewModeClicked(object sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new ReviewPage());
    }

    // 闯关回调，isSuccess: true=成功，false=失败或主动退出
    private void OnQuizRoundFinished(bool isSuccess, int correctCount, int totalCount)
    {
        _quizRecords.Insert(0, new QuizRecord
        {
            Attempt = _quizRecords.Count + 1,
            CorrectCount = correctCount,
            TotalCount = totalCount,
            IsSuccess = isSuccess
        });
        SaveQuizRecords(); // 新增
        UpdateQuizRecordsView();
        string exitReason = Preferences.Get("QuizExitReason", "");

        string Pick(string[] pool) => pool[_rand.Next(pool.Length)];

        if (exitReason == "exit")
        {
            Preferences.Set("QuizExitReason", "");
            WelcomeLabel.Text = Pick(FailureMessages);
        }
        else if (isSuccess)
        {
            WelcomeLabel.Text = Pick(SuccessMessages);
        }
        else
        {
            WelcomeLabel.Text = Pick(FailureMessages);
        }
        SonModeButton.IsEnabled = true;
    }

    private void LoadQuizRecords()
    {
        var json = Preferences.Get("QuizRecords", "");
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                _quizRecords = JsonSerializer.Deserialize<List<QuizRecord>>(json) ?? new List<QuizRecord>();
            }
            catch
            {
                _quizRecords = new List<QuizRecord>();
            }
        }
        else
        {
            _quizRecords = new List<QuizRecord>();
        }
    }

    private void LoadQuizHistories()
    {
        string file = Path.Combine(FileSystem.AppDataDirectory, "QuizHistory.json");
        if (File.Exists(file))
        {
            try
            {
                var json = File.ReadAllText(file);
                _quizHistories = JsonSerializer.Deserialize<List<QuizHistory>>(json) ?? new();
            }
            catch { _quizHistories = new(); }
        }
        else
        {
            _quizHistories = new();
        }
    }

    private void UpdateQuizRecordsView()
    {
        QuizRecordsLayout.Children.Clear();
        LoadQuizHistories();
        int showCount = Math.Min(_quizHistories.Count,13); // 最多显示15条历史
        for (int i =0; i < showCount; i++)
        {
            var h = _quizHistories[i];
            string text = $"第{_quizHistories.Count - i}次 闯关：答对{h.CorrectCount}个单词。";
            if (h.IsSuccess && h.CorrectCount == h.TotalCount)
                text += "全部正确！";
            var label = new Label { Text = text, FontSize =16 };
            int historyIndex = i;
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) =>
            {
                string detail = string.Join("\n", h.Meanings.Select((m, idx) => $"{idx +1}. 【{m}】\n标准答案: {h.Answers[idx]}\n你的答案: {(idx < h.UserAnswers.Count ? h.UserAnswers[idx] : "(未作答)")}"));
                await DisplayAlert($"第{_quizHistories.Count - historyIndex}次考试详情", detail, "关闭");
            };
            label.GestureRecognizers.Add(tapGesture);
            QuizRecordsLayout.Children.Add(label);
        }
    }

    private void SaveQuizRecords()
    {
        var json = JsonSerializer.Serialize(_quizRecords);
        Preferences.Set("QuizRecords", json);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        App.SetStatusBarBlackText();
#endif
    }
}