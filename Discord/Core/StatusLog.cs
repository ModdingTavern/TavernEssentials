namespace TavernEssentials.Discord
{
    internal sealed class StatusLog
    {
        Status _place;
        int _quiet;
        string _refusing;
        int _refused;

        internal string Sending(Status s, string game)
        {
            if (!StatusText.NewPlace(_place, s))
            {
                _quiet++;
                return null;
            }
            string line = "status: " + s.Describe() + " (the game had " + game + ")"
                + (_quiet == 0 ? "" : "; " + _quiet + " more went out at the last place") + ".";
            _place = s;
            _quiet = 0;
            return line;
        }

        internal string Answered(bool told, bool ok, string result)
        {
            if (ok)
            {
                if (_refusing != null)
                {
                    string back = "Discord takes it again, after " + _refused + " refused.";
                    _refusing = null;
                    _refused = 0;
                    return back;
                }
                return told ? "Discord took it." : null;
            }
            _refused++;
            bool news = told || result != _refusing;
            _refusing = result;
            return news ? "Discord refused it: " + result + "." : null;
        }
    }
}
