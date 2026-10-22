namespace TavernEssentials.Board
{
    static class Scenes
    {
        public const int Overworld = 0;
        public const int Tutorial = 1;
        public const int Quest = 4;

        public static int For(bool quest, bool tutorial)
        {
            if (tutorial) return Tutorial;
            if (quest) return Quest;
            return Overworld;
        }

        public static string Name(int index)
        {
            if (index == Tutorial) return "tutorial scene";
            if (index == Quest) return "quest overworld";
            if (index == Overworld) return "standard overworld";
            return "scene " + index;
        }
    }
}
