namespace 爸爸的爱;

public sealed class RecordDetailPage : ContentPage
{
 public RecordDetailPage(string title, string content)
 {
 BackgroundColor = (Color)Application.Current.Resources["AppColorPageBackground"];
 var titleLabel = new Label
 {
 Text = title,
 FontSize =18, // 标题字号统一
 TextColor = (Color)Application.Current.Resources["AppColorTextPrimary"],
 LineBreakMode = LineBreakMode.WordWrap
 };
 var contentLabel = new Label
 {
 Text = content,
 FontSize =16,
 TextColor = (Color)Application.Current.Resources["AppColorTextSecondary"],
 LineBreakMode = LineBreakMode.WordWrap
 };
 var scroll = new ScrollView
 {
 Content = contentLabel,
 VerticalOptions = LayoutOptions.FillAndExpand
 };
 var closeBtn = new Button
 {
 Text = "关闭",
 WidthRequest =160,
 HorizontalOptions = LayoutOptions.Center,
 BackgroundColor = (Color)Application.Current.Resources["AppColorButtonBackground"],
 TextColor = (Color)Application.Current.Resources["AppColorButtonText"],
 Margin = new Thickness(0,8)
 };
 closeBtn.Clicked += async (s, e) => await Navigation.PopModalAsync();

 var grid = new Grid
 {
 Padding = new Thickness(16),
 RowDefinitions =
 {
 new RowDefinition(GridLength.Auto), // Title
 new RowDefinition(GridLength.Star), // Scrollable content
 new RowDefinition(GridLength.Auto) // Close button stays visible
 }
 };
 grid.Add(titleLabel,0,0);
 grid.Add(scroll,0,1);
 grid.Add(closeBtn,0,2);

 Content = grid;
 }
}
