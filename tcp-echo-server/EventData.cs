using System;

namespace tcp_echo_server
{
    /// <summary>The text the server pushes random fragments of to connected clients.</summary>
    internal static class EventData
    {
        public const string Text =
@"My glass shall not persuade me I am old,
So long as youth and thou are of one date;
But when in thee time’s furrows I behold,
Then look I death my days should expiate.
For all that beauty that doth cover thee,
Is but the seemly raiment of my heart,
Which in thy breast doth live, as thine in me:
How can I then be elder than thou art?
O! therefore love, be of thyself so wary
As I, not for myself, but for thee will;
Bearing thy heart, which I will keep so chary
As tender nurse her babe from faring ill.
Presume not on thy heart when mine is slain,
Thou gav’st me thine not to give back again.
";

        public static readonly string[] Lines =
            Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }
}
