using System;
using Manos.IO;
using Mono.Terminal;

namespace ColorTest
{
	class ColorWidget : Widget
	{
		int start = 0;
		public int Start {
			get {
				return start;
			}
			set {
				int max = Curses.Colors - Height;
				if (value < 0) {
					start = 0;
				} else if (value > max) {
					start = max;
				} else {
					start = value;
				}
			}
		}

		int TerminalHeight {
			get {
				return (int)(Curses.Terminal.Height * 0.8);
			}
		}

		public override bool ProcessKey (int key)
		{
			switch (key) {
			case 338:
				Start += TerminalHeight;
				Invalid = true;
				return true;
			case 339:
				Start -= TerminalHeight;
				Invalid = true;
				return true;
			case 259:
				Start -= 1;
				Invalid = true;
				return true;
			case 258:
				Start += 1;
				Invalid = true;
				return true;
			default:
				return base.ProcessKey(key);
			}

		}

		string FillSpace(int number)
		{
			return FillSpace(number, Curses.Colors);
		}

		string FillSpace(int number, int max)
		{
			int length = max.ToString().Length;
			string str = number.ToString();
			while (str.Length < length) {
				str = " " + str;
			}
			return str;
		}

		public override void Redraw()
		{
			// Hack if you need more than 256 colors
			ColorPair.ReleaseAll();
			for (int i = 0; i < Curses.Colors; i++) {
				if (i >= Height) {
					break;
				}

				int y = start + i;

				string str = string.Format("\x0000{0} {1} \x0000{0},{0} ", y, FillSpace(y));
				ColorString.Fill(this, str, 0, i, Width, 1);
			}
		}
	}

	class MainClass
	{
		public static void Main (string[] args)
		{
			Application.Init(Context.Create(Backend.Poll));
			Application.Run(new FullsizeContainer(new ColorWidget()));
		}
	}
}

