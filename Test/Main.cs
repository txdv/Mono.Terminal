using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
//using Mono.Unix.Native;
using Mono.Terminal;
using Mono.Terminal.Poll;

namespace Test
{
	class Filler : Widget
	{
		public char Char { get; set; }

		public Filler(char ch)
		{
			Char = ch;
		}
		public override void Redraw()
		{
			for (int x = 0; x < Width; x++) {
				for (int y = 0; y < Height; y++) {
					Set(x, y, Char);
				}
			}
		}

		public override bool ProcessKey(int key)
		{
			return false;
		}

		public override void SetCursorPosition()
		{
			Curses.Cursor.Move(0, 0);
		}
	}

	public class ViewPortInfo
	{
		public DateTime DateTime { get; set; }
		public string Nick { get; set; }
		public string Message { get; set; }
		public string OP { get; set; }
	}
	public static class ACS
	{
		public const int LLCORNER = unchecked((int)0x40006d);
		public const int LRCORNER = unchecked((int)0x40006a);
		public const int HLINE    = unchecked((int)0x400071);
		public const int ULCORNER = unchecked((int)0x40006c);
		public const int URCORNER = unchecked((int)0x40006b);
		public const int VLINE    = unchecked((int)0x400078);
	}

	public class ColorString
	{
		enum DrawStringMode {
			Normal,
			ForegroundColor,
			Background
		}

		public static int Length(string str)
		{
			DrawStringMode mode = DrawStringMode.Normal;
			int len = 0;
			foreach (char c in str) {
				switch (mode) {
				case DrawStringMode.Normal:
					if (c == '\x0000') {
						mode = DrawStringMode.ForegroundColor;
					} else {
						len++;
					}
					break;
				case DrawStringMode.ForegroundColor:
					if (c == ' ') {
						mode = DrawStringMode.Normal;
					}
					break;
				}
			}
			return len;
		}

		public static int DrawString(string str)
		{
			return DrawString(str, delegate (char ch) {
				Curses.Add(ch);
			});
		}

		public static int DrawString(string str, Action<char> callback)
		{
			int n = 0;

			DrawStringMode mode = DrawStringMode.Normal;

			string fg = string.Empty;
			string bg = string.Empty;

			foreach (char c in str) {
				switch (mode) {
				case DrawStringMode.Normal:
					if (c == '\x0000') {
						mode = DrawStringMode.ForegroundColor;
					} else {
						callback(c);
						n++;
					}
					break;
				case DrawStringMode.ForegroundColor:
					if (char.IsNumber(c)) {
						fg += c;
					} else if (c == ',') {
						mode = DrawStringMode.Background;
					} else if (c == ' ') {
						ushort foreground = ushort.MaxValue;
						if (fg != string.Empty) {
							foreground = ushort.Parse(fg);
						}
						Curses.attron(ColorPair.From(foreground, ushort.MaxValue).Attribute);
						fg = string.Empty;
						mode = DrawStringMode.Normal;
					} else {
						throw new Exception("Malformed color string");
					}
					break;
				case DrawStringMode.Background:
					if (char.IsNumber(c)) {
						bg += c;
					} else if (c == ' ') {
						Curses.attron(ColorPair.From(ushort.Parse(fg), ushort.Parse(bg)).Attribute);
						fg = string.Empty;
						bg = string.Empty;
						mode = DrawStringMode.Normal;
					} else {
						throw new Exception("Malformed color string");
					}
					break;
				}
			}
			return n;
		}
	}

	public class ViewPortEntry : Widget
	{
		public ViewPort ViewPort { get; internal set; }
		public ViewPortEntry Previous { get; internal set; }

		public string String { get; protected set; }

		public ViewPortInfo Info { get; protected set; }
		public ViewPortEntry(ViewPortInfo info)
		{
			Info = info;

			int AccentColor = 202;

			String = GetTime(AccentColor) + ' ' + GetNick(AccentColor, '+') + "\x0000255  " + Info.Message;
		}

		public int GetHeight(int width)
		{
			return (int)Math.Ceiling((double)ColorString.Length(String) / width);
		}

		public int GetFillout(int width)
		{
			return (GetHeight(width) * width) - ColorString.Length(String);
		}

		string Empty(int length)
		{
			string ret = string.Empty;
			for (int i = 0; i < length; i++) {
				ret += " ";
			}
			return ret;
		}

		public string GetTime(int color)
		{
			return string.Format("\x0000241 (\x0000255 {1}\x0000{0} :\x0000255 {2}\x0000{0} :\x0000255 {3}am\x0000241 )",
				color,
				Info.DateTime.ToString("hh"),
				Info.DateTime.ToString("mm"),
				Info.DateTime.ToString("ss")
			);
		}

		public string GetNick(int color, char op) {
			return string.Format("\x0000241 (\x0000{0} {1}\x0000255 {2}\x0000241 )",
				color,
				op,
				Info.Nick
			);
		}

		public override void Redraw()
		{
			Fill(' ');

			Move(0, 0);

			string res = String + string.Format("\x0000{0} ", 255) + Empty(GetFillout(Width));

			int i = 0;
			int x = 0, y = 0;
			ColorString.DrawString(res, delegate (char ch) {
				Set(x, y, ch);
				x++;
				if (x >= Width) {
					x = 0;
					y++;
				}
				i++;
			});
		}

		public int Length { get; protected set; }
	}

	public class ViewPort : Widget
	{
		public ViewPort()
		{
			PageFactor = 0.8;
		}

		private int position;
		public int Position {
			get {
				return position;
			}
			set {
				if (position == value) {
					return;
				}

				Invalid = true;
				position = value;
				if (position < MinimumPosition) {
					position = MinimumPosition;
				} else if (position > MaximumPosition) {
					position = MaximumPosition;
				}
			}
		}

		public override void SetDim(int x, int y, int w, int h)
		{
			base.SetDim(x, y, w, h);

			CalculateTotaleHeight();
		}

		public int MaximumPosition {
			get {
				return TotalHeight - Height;
			}
		}

		public int MinimumPosition {
			get {
				return 0;
			}
		}

		public double PageFactor { get; set; }

		public int TotalHeight { get; protected set; }

		public void Add(ViewPortEntry entry)
		{
			Invalid = true;

			entry.ViewPort = this;
			if (entries.Last != null) {
				entry.Previous = entries.Last.Value;
			}
			entries.AddLast(entry);
		}

		private LinkedList<ViewPortEntry> entries = new LinkedList<ViewPortEntry>();

		public override bool ProcessKey(int key)
		{
			switch (key) {
			case 339:
				if (TotalHeight - Position > Height) {
					Position += (int)(Height * PageFactor);
				}
				return true;
			case 338:
				if (Position > MinimumPosition) {
					Position -= (int)(Height * PageFactor);
				}
				return true;
			default:
				return false;
			}
		}

		private void CalculateTotaleHeight()
		{
			TotalHeight = 0;
			for (var element = entries.First; element != null; element = element.Next) {
				var entry = element.Value;
				TotalHeight += entry.GetHeight(Width);
			}
		}

		public override void Redraw()
		{
			if (Invalid) {
				Invalid = false;
			} else {
				return;
			}

			int x = X;
			int h = 0;

			ViewPortEntry entry;

			if (TotalHeight > Height) {
				// we have just to pait a certain *window* of the entire list
				h = Height;
				for (var element = entries.Last; element != null; element = element.Previous) {
					entry = element.Value;

					entry.Width = Width;
					entry.Height = entry.GetHeight(Width);
					h -= entry.Height;
					entry.X = x;
					entry.Y = h + Position;

					if (entry.Y + entry.Height < 0) {
						return;
					}

					entry.Redraw();
				}
			} else {
				// everything fills in, just paint it
				for (var element = entries.First; element != null; element = element.Next) {
					entry = element.Value;

					entry.X = x;
					entry.Y = h;
					entry.Width = Width;
					entry.Height = entry.GetHeight(Width);

					entry.Redraw();

					h += entry.Height;
				}
			}
		}
	}

	public class Entry : Widget
	{
		public int Position { get; set; }

		public Entry()
			: base()
		{
			Text = "";
			Position = 0;
			History = new LinkedList<string>();
		}

		public string Text { get; set; }

		public override bool CanFocus {
			get {
				return true;
			}
		}

		public void Clear()
		{
			for (int x = 0; x < Width; x++) {
				for (int y = 0; y < Height; y++) {
					Set(X, Y, ' ');
				}
			}
		}

		public override void Redraw()
		{
			for (int x = 0; x < Width; x++ ) {
				if (x < Text.Length) {
					Set(x, 0, Text[x]);
				} else {
					Set(x, 0, ' ');
				}
			}
			Move(Position, 0);
		}

		protected int GetFirstNonWhiteSpace()
		{
			int i = Position;
			while (i >= Text.Length) {
				i--;
			}

			for (; i > 0; i--) {
				if (Text[i] != ' ') {
					return i;
				}
			}

			return 0;
		}

		protected int GetFirstWhiteSpace()
		{
			int i = Position;
			while (i >= Text.Length) {
				i--;
			}

			for (; i > 0; i--) {
				if (Text[i] == ' ') {
					return i;
				}
			}

			return 0;
		}

		protected int GetFirst()
		{
			if (Text[Position - 1] == ' ') {
				return GetFirstNonWhiteSpace();
			} else {
				return GetFirstWhiteSpace();
			}
		}

		public void AddHistory(string text)
		{
			History.AddLast(text);

			CurrentHistoryElement = null;
		}

		private LinkedList<string> History { get; set; }

		private LinkedListNode<string> CurrentHistoryElement { get; set; }

		public string Current {
			get {
				if (CurrentHistoryElement.Value == null) {
					return string.Empty;
				} else {
					return CurrentHistoryElement.Value;
				}
			}
		}

		public string PrevHistory()
		{
			if (CurrentHistoryElement == null) {
				if (History.Last == null) {
					return string.Empty;
				}
				CurrentHistoryElement = History.Last;
			} else {
				if (CurrentHistoryElement.Previous != null) {
					CurrentHistoryElement = CurrentHistoryElement.Previous;
				}
			}
			return CurrentHistoryElement.Value;
		}

		public string NextHistory()
		{
			if (CurrentHistoryElement == null) {
				if (History.First == null) {
					return string.Empty;
				}
				CurrentHistoryElement = History.Last;
			} else {
				if (CurrentHistoryElement.Next != null) {
					CurrentHistoryElement = CurrentHistoryElement.Next;
				}
			}
			return CurrentHistoryElement.Value;
		}

		public override bool ProcessKey(int key)
		{
			char ch = (char)key;

			System.Console.Error.WriteLine(key);

			switch (key) {
			case (int)'a' - 96:
				Position = 0;
				return true;
			case (int)'e' - 96:
				Position = Text.Length;
				return true;
			case (int)'w' - 96:
				if (Position == 1) {
					Text = Text.Substring(1);
					Position = 0;
				} else if (Position != 0) {
					int i = GetFirst() + 1;
					if (i == 1) {
						Text = Text.Substring(Position);
						Position = 0;
					} else {
						Text = Text.Substring(0, i);
						Position = i;
					}
				}
				return true;
			case 126:
				if (Position < Text.Length) {
					Text = Text.Substring(0, Text.Length - 1);
				}
				return true;
			case 127:
			case Curses.Key.Backspace:
				if (Position > 0) {
					Text = Text.Substring(0, Position - 1) + Text.Substring(Position);
					Position--;
				}
				return true;
			case Curses.Key.Left:
				if (Position > 0) {
					Position--;
				}
				return true;
			case Curses.Key.Right:
				if (Position < Text.Length) {
					Position++;
				}
				return true;
			case Curses.Key.Delete:
				if (Position < Text.Length) {
					Text = Text.Substring(0, Position) + Text.Substring(Position + 1);
				}
				return true;
			case Curses.Key.Up:
				Text = PrevHistory();
				Position = Text.Length;
				return true;
			case Curses.Key.Down:
				Text = NextHistory();
				Position = Text.Length;
				return true;
			case 10:
				if (Enter != null) {
					Enter();
				}
				return true;
			default:
				if (key < 32 || key > 255) {
					return false;
				} else {
					Text = Text.Substring(0, Position) + ch + Text.Substring(Position);
					Position++;
					return true;
				}
			}
		}

		public override void SetCursorPosition()
		{
			Move(0, Position);
		}

		public event Action Enter;
	}

	public class StatusBar : Widget
	{
		public static ColorPair Color { get; protected set; }
		public StatusBar()
		{
			Color = ColorPair.From(ushort.MaxValue, 237);
		}

		public override void Redraw()
		{
			Curses.attron(Curses.Attributes.Bold);
			Curses.attron(Color.Attribute);

			Fill(' ');

			//Move(0, 0);

			Set(0, 0, "This is some awesome biatch ass mofo");

			Curses.attroff(Color.Attribute);
			Curses.attroff(Curses.Attributes.Bold);
		}

		public void DrawString(string str, int fg, int bg)
		{

		}
	}

	public class IrssiControl : VBox
	{
		public ViewPort ViewPort { get; protected set; }
		public StatusBar StatusBar { get; protected set; }
		public Entry Entry { get; protected set; }

		public IrssiControl()
		{
			ViewPort = new ViewPort();
			StatusBar = new StatusBar() { Height = 1 };
			Entry = new Entry() { Height = 1 };

			Entry.Enter += delegate {
				if (Entry.Text.Length == 0) {
					return;
				}

				string text = Entry.Text.TrimEnd(new char [] { ' ', '\t' });

				if (text.StartsWith("/quit")) {
					Application.Exit = true;
				} else {
					ViewPort.Add(new ViewPortEntry(new ViewPortInfo() {
						DateTime = DateTime.Now,
						Nick = "ToXedVirus",
						Message = text
					}));
					ViewPort.Position = ViewPort.MinimumPosition;
				}

				Entry.AddHistory(text);

				Entry.Text = "";
				Entry.Position = 0;
			};

			this.Add(ViewPort, Box.Setting.Fill);
			this.Add(StatusBar, Box.Setting.Size);
			this.Add(Entry, Box.Setting.Size);
		}

		public override bool ProcessKey(int key)
		{
			switch (key) {
			case 338:
			case 339:
				return ViewPort.ProcessKey(key);
			default:
				return base.ProcessKey(key);
			}
		}
	}

	class MainClass
	{
		public static void Main(string[] args)
		{
			Application.Init();

			Loop loop = new Loop();
			PollKeyDispatcher pkd = new PollKeyDispatcher(loop);


			var irssi = new IrssiControl();

			for (int i = 0; i < 20000; i++) {
				irssi.ViewPort.Add(new ViewPortEntry(new ViewPortInfo() {
					DateTime = DateTime.Now,
					Nick = "ToXedVirus",
					Message = "A very long test string! " + i
					//Message = string.Format("{0};{1}", Curses.Cursor.X, Curses.Cursor.Y)
				}));
			}

			Application.Run(pkd, irssi);
		}

		public static string str(string str, int n)
		{
			string ret = string.Empty;
			for (int i = 0; i < n; i++) {
				ret += str;
			}
			return ret;
		}
	}
}