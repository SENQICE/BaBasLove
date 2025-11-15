using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using System.Threading.Tasks;
#if ANDROID
using Android.Views;
#endif

namespace 爸爸的爱;

public partial class SettingsPage : ContentPage
{
    // 单元信息
    private readonly List<(string Book, string Unit, Switch Switch)> _unitSwitches;

    public SettingsPage(Action onQuizRecordsCleared = null)
    {
        InitializeComponent();
        _onQuizRecordsCleared = onQuizRecordsCleared;

        //绑定所有单元开关，便于统一操作
        _unitSwitches = new()
        {
            // 三年级上
            ("三年级上册", "第一单元", Unit1_3UpSwitch),
            ("三年级上册", "第二单元", Unit2_3UpSwitch),
            ("三年级上册", "第三单元", Unit3_3UpSwitch),
            ("三年级上册", "第四单元", Unit4_3UpSwitch),
            // 三年级下
            ("三年级下册", "第一单元", Unit1_3DownSwitch),
            ("三年级下册", "第二单元", Unit2_3DownSwitch),
            ("三年级下册", "第三单元", Unit3_3DownSwitch),
            ("三年级下册", "第四单元", Unit4_3DownSwitch),
            // 四年级上
            ("四年级上册", "第一单元", Unit1_4UpSwitch),
            ("四年级上册", "第二单元", Unit2_4UpSwitch),
            ("四年级上册", "第三单元", Unit3_4UpSwitch),
            ("四年级上册", "第四单元", Unit4_4UpSwitch),
            // 四年级下
            ("四年级下册", "第一单元", Unit1_4DownSwitch),
            ("四年级下册", "第二单元", Unit2_4DownSwitch),
            ("四年级下册", "第三单元", Unit3_4DownSwitch),
            ("四年级下册", "第四单元", Unit4_4DownSwitch),
            // 五年级上
            ("五年级上册", "第一单元", Unit1UpSwitch),
            ("五年级上册", "第二单元", Unit2UpSwitch),
            ("五年级上册", "第三单元", Unit3UpSwitch),
            ("五年级上册", "第四单元", Unit4UpSwitch),
            // 五年级下
            ("五年级下册", "第一单元", Unit1DownSwitch),
            ("五年级下册", "第二单元", Unit2DownSwitch),
            ("五年级下册", "第三单元", Unit3DownSwitch),
            ("五年级下册", "第四单元", Unit4DownSwitch),
            // 六年级上
            ("六年级上册", "第一单元", Unit1_6UpSwitch),
            ("六年级上册", "第二单元", Unit2_6UpSwitch),
            ("六年级上册", "第三单元", Unit3_6UpSwitch),
            ("六年级上册", "第四单元", Unit4_6UpSwitch),
            // 六年级下
            ("六年级下册", "第一单元", Unit1_6DownSwitch),
            ("六年级下册", "第二单元", Unit2_6DownSwitch),
            ("六年级下册", "第三单元", Unit3_6DownSwitch),
            ("六年级下册", "第四单元", Unit4_6DownSwitch),
        };

        LoadSettings();
    }

    private readonly Action _onQuizRecordsCleared;

    private void LoadSettings()
    {
        //只在首次启动时设置默认值，否则始终读取保存值
        bool isFirstRun = !Preferences.ContainsKey("HasRunBefore");
        if (isFirstRun)
        {
            Preferences.Set("HasRunBefore", true);
            Preferences.Set("Grade3Up", false);
            Preferences.Set("Grade3Down", false);
            Preferences.Set("Grade4Up", false);
            Preferences.Set("Grade4Down", false);
            Preferences.Set("GradeUp", false);
            Preferences.Set("GradeDown", false);
            Preferences.Set("Grade6Up", true);
            Preferences.Set("Grade6Down", false);
            Preferences.Set("WrittenMode", false);
            Preferences.Set("六年级上册_第一单元", true);
            //其他单元全部关闭
            foreach (var (book, unit, sw) in _unitSwitches)
            {
                string key = $"{book}_{unit}";
                if (book == "六年级上册" && unit == "第一单元")
                    Preferences.Set(key, true);
                else
                    Preferences.Set(key, false);
            }
        }
        // 年级开关直接读取保存值
        Grade3UpSwitch.IsToggled = Preferences.Get("Grade3Up", false);
        Grade3DownSwitch.IsToggled = Preferences.Get("Grade3Down", false);
        Grade4UpSwitch.IsToggled = Preferences.Get("Grade4Up", false);
        Grade4DownSwitch.IsToggled = Preferences.Get("Grade4Down", false);
        GradeUpSwitch.IsToggled = Preferences.Get("GradeUp", false);
        GradeDownSwitch.IsToggled = Preferences.Get("GradeDown", false);
        Grade6UpSwitch.IsToggled = Preferences.Get("Grade6Up", false);
        Grade6DownSwitch.IsToggled = Preferences.Get("Grade6Down", false);

        foreach (var (book, unit, sw) in _unitSwitches)
        {
            string key = $"{book}_{unit}";
            sw.IsToggled = Preferences.Get(key, false); // 不再默认 true

            //由所属年级的总开关控制是否可用
            bool gradeEnabled = book.Contains("三年级上册") ? Grade3UpSwitch.IsToggled
                : book.Contains("三年级下册") ? Grade3DownSwitch.IsToggled
                : book.Contains("四年级上册") ? Grade4UpSwitch.IsToggled
                : book.Contains("四年级下册") ? Grade4DownSwitch.IsToggled
                : book.Contains("五年级上册") ? GradeUpSwitch.IsToggled
                : book.Contains("五年级下册") ? GradeDownSwitch.IsToggled
                : book.Contains("六年级上册") ? Grade6UpSwitch.IsToggled
                : Grade6DownSwitch.IsToggled;

            sw.IsEnabled = gradeEnabled;
            if (!gradeEnabled) sw.IsToggled = false;
        }

        QuestionCountEntry.Text = Preferences.Get("QuestionCount", 8).ToString();
        TimePerQuestionEntry.Text = Preferences.Get("TimePerQuestion", 30).ToString();
        WrittenModeSwitch.IsToggled = Preferences.Get("WrittenMode", false); //直接读取保存值
    }

    private void OnGrade3UpToggled(object sender, ToggledEventArgs e) { ToggleUnitsByBookPrefix("三年级上册", e.Value); }
    private void OnGrade3DownToggled(object sender, ToggledEventArgs e) { ToggleUnitsByBookPrefix("三年级下册", e.Value); }
    private void OnGrade4UpToggled(object sender, ToggledEventArgs e) { ToggleUnitsByBookPrefix("四年级上册", e.Value); }
    private void OnGrade4DownToggled(object sender, ToggledEventArgs e) { ToggleUnitsByBookPrefix("四年级下册", e.Value); }
    private void OnGradeUpToggled(object sender, ToggledEventArgs e) { ToggleUnitsByBookPrefix("五年级上册", e.Value); }
    private void OnGradeDownToggled(object sender, ToggledEventArgs e) { ToggleUnitsByBookPrefix("五年级下册", e.Value); }
    private void OnGrade6UpToggled(object sender, ToggledEventArgs e) { ToggleUnitsByBookPrefix("六年级上册", e.Value); }
    private void OnGrade6DownToggled(object sender, ToggledEventArgs e) { ToggleUnitsByBookPrefix("六年级下册", e.Value); }
    private void OnUnitSwitchToggled(object sender, ToggledEventArgs e) { /* 不做任何保存 */ }

    private void SaveSettingsSilently()
    {
        Preferences.Set("Grade3Up", Grade3UpSwitch.IsToggled);
        Preferences.Set("Grade3Down", Grade3DownSwitch.IsToggled);
        Preferences.Set("Grade4Up", Grade4UpSwitch.IsToggled);
        Preferences.Set("Grade4Down", Grade4DownSwitch.IsToggled);
        Preferences.Set("GradeUp", GradeUpSwitch.IsToggled);
        Preferences.Set("GradeDown", GradeDownSwitch.IsToggled);
        Preferences.Set("Grade6Up", Grade6UpSwitch.IsToggled);
        Preferences.Set("Grade6Down", Grade6DownSwitch.IsToggled);
        Preferences.Set("WrittenMode", WrittenModeSwitch.IsToggled);
        foreach (var (book, unit, sw) in _unitSwitches)
        {
            Preferences.Set($"{book}_{unit}", sw.IsToggled);
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // 年级保存
        Preferences.Set("Grade3Up", Grade3UpSwitch.IsToggled);
        Preferences.Set("Grade3Down", Grade3DownSwitch.IsToggled);
        Preferences.Set("Grade4Up", Grade4UpSwitch.IsToggled);
        Preferences.Set("Grade4Down", Grade4DownSwitch.IsToggled);
        Preferences.Set("GradeUp", GradeUpSwitch.IsToggled);
        Preferences.Set("GradeDown", GradeDownSwitch.IsToggled);
        Preferences.Set("Grade6Up", Grade6UpSwitch.IsToggled);
        Preferences.Set("Grade6Down", Grade6DownSwitch.IsToggled);
        Preferences.Set("WrittenMode", WrittenModeSwitch.IsToggled);

        int selectedUnitCount =0;
        foreach (var (book, unit, sw) in _unitSwitches)
        {
            string key = $"{book}_{unit}";
            Preferences.Set(key, sw.IsToggled);
            if (sw.IsToggled) selectedUnitCount++;
        }

        // 校验题数
        int questionCount =8;
        if (!int.TryParse(QuestionCountEntry.Text, out questionCount) || questionCount <8)
            questionCount =8;
        if (selectedUnitCount ==0)
        {
            Preferences.Set("六年级上册_第一单元", true);
            selectedUnitCount =1;
        }
        Preferences.Set("QuestionCount", questionCount);

        // 校验时间
        int timePerQuestion =30;
        if (!int.TryParse(TimePerQuestionEntry.Text, out timePerQuestion) || timePerQuestion <5)
            timePerQuestion =30;
        Preferences.Set("TimePerQuestion", timePerQuestion);

        await DisplayAlert("提示", "设置已保存", "确定");
        await Navigation.PopModalAsync(); // 保存后直接返回主界面
    }

    // 静态方法供 QuizPage 调用
    public static List<(string book, string unit)> GetSelectedUnits()
    {
        var result = new List<(string, string)>();
        var books = new[] { "三年级上册", "三年级下册", "四年级上册", "四年级下册", "五年级上册", "五年级下册", "六年级上册", "六年级下册" };
        var units = new[] { "第一单元", "第二单元", "第三单元", "第四单元" };
        foreach (var book in books)
        {
            //只有年级总开关打开才允许单元被选
            bool gradeEnabled = Preferences.Get(book switch {
                "三年级上册" => "Grade3Up",
                "三年级下册" => "Grade3Down",
                "四年级上册" => "Grade4Up",
                "四年级下册" => "Grade4Down",
                "五年级上册" => "GradeUp",
                "五年级下册" => "GradeDown",
                "六年级上册" => "Grade6Up",
                "六年级下册" => "Grade6Down",
                _ => "Grade6Up"
            }, book == "六年级上册");
            if (!gradeEnabled) continue;
            foreach (var unit in units)
            {
                if (Preferences.Get($"{book}_{unit}", book == "六年级上册"))
                    result.Add((book, unit));
            }
        }
        return result;
    }

    public static int GetQuestionCount() => Preferences.Get("QuestionCount", 8);
    public static int GetTimePerQuestion() => Preferences.Get("TimePerQuestion", 30);

    private async void OnClearQuizRecordsClicked(object sender, EventArgs e)
    {
        Preferences.Remove("QuizRecords");
        await DisplayAlert("提示", "答题记录已清除", "确定");
        _onQuizRecordsCleared?.Invoke();
    }

    private async void OnChangePasswordClicked(object sender, EventArgs e)
    {
        string currentPwd = Preferences.Get("ParentPassword", "123456");
        string oldPwd = await DisplayPromptAsync(
            "更改密码",
            "请输入当前家长密码",
            "确定",
            "取消",
            "",
            -1,
            Keyboard.Text
        );
        if (oldPwd == null) return; // 用户取消
        if (oldPwd != currentPwd)
        {
            await DisplayAlert("错误", "当前密码错误", "确定");
            return;
        }
        string newPwd = await DisplayPromptAsync(
                "更改密码",
                "请输入新密码",
                "确定",
                "取消",
                "",
                -1,
                Keyboard.Text
            );
        if (string.IsNullOrEmpty(newPwd)) return;
        Preferences.Set("ParentPassword", newPwd);
        await DisplayAlert("提示", "家长密码已更改", "确定");
    }

    private void ToggleUnitsByBookPrefix(string book, bool isOn)
    {
        foreach (var (b, unit, sw) in _unitSwitches)
        {
            if (b == book)
            {
                sw.IsEnabled = isOn;
                if (!isOn) sw.IsToggled = false;
            }
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        // 第一次：等一帧，确保页面已附着到窗口
        await Task.Delay(16);
        // 主动设为透明，显示系统/主题原本颜色，避免黑字不生效
        App.SetStatusBarTransparent();
        // 第二次：等模态上滑动画结束后再设一次
        Device.StartTimer(TimeSpan.FromMilliseconds(260), () =>
        {
            App.SetStatusBarTransparent();
            return false; //仅执行一次
        });
#endif
    }
}