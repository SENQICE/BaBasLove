using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using System.Threading.Tasks;
#if ANDROID
using Android.Views;
#endif

namespace 爸爸的爱;

public partial class ReviewPage : ContentPage
{
 private List<Vocab> _reviewList;
 private Random _rand = new();

 public ReviewPage()
 {
 InitializeComponent();
 // 获取当前设置范围的所有词汇
 var selectedUnits = SettingsPage.GetSelectedUnits();
 _reviewList = VocabRepository.AllVocabs
 .Where(v => selectedUnits.Any(su => su.book == v.Book && su.unit == v.Unit))
 .ToList();
 ShowRandomVocab();
 }

 protected override async void OnAppearing()
 {
 base.OnAppearing();
 #if ANDROID
 // 延后一帧，确保在模态页面完全展示后再设置
 await Task.Delay(16);
 App.SetStatusBarTransparent();
 Device.StartTimer(TimeSpan.FromMilliseconds(260), () =>
 {
 App.SetStatusBarTransparent();
 return false;
 });
 #endif
 }

 private void ShowRandomVocab()
 {
 if (_reviewList.Count ==0)
 {
 WordLabel.Text = "无词汇";
 MeaningLabel.Text = "请在家长设置中选择单元";
 PhoneticLabel.Text = "";
 return;
 }
 var vocab = _reviewList[_rand.Next(_reviewList.Count)];
 WordLabel.Text = vocab.Word;
 MeaningLabel.Text = vocab.Meaning;
 PhoneticLabel.Text = string.IsNullOrWhiteSpace(vocab.Phonetic) ? "" : $"音标：{vocab.Phonetic}";
 }

 private void OnNextClicked(object sender, EventArgs e)
 {
 ShowRandomVocab();
 }

 private async void OnExitClicked(object sender, EventArgs e)
 {
 await Navigation.PopModalAsync();
 }
}
