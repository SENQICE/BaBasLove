using Android.App;
using Android.Content;
using 爸爸的爱;

[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context context, Intent intent)
    {
        if (intent.Action == Intent.ActionBootCompleted)
        {
            Intent startIntent = new(context, typeof(MainActivity));
            startIntent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(startIntent);
        }
    }
}
