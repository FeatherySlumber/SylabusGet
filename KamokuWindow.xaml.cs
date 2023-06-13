using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace ClassTimetableToSyllabus
{
    /// <summary>
    /// KamokuWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class KamokuWindow : Window
    {
        public KamokuWindow(Kamoku kamoku, string[] texts)
        {
            InitializeComponent();
            this.DataContext = kamoku;
            PageList.ItemsSource = texts.Select((i,n) => new {Text = i, Page = (n + kamoku.StartPage).ToString() + "ページ目" }).ToArray();
            PageList.SelectedIndex = texts.Length > 1 ? 0 : -1;
        }
    }

    public class Kamoku : INotifyPropertyChanged
    {
        public Kamoku(PdfDocument pd)
        {
            EditCommand = new((kamoku) => {
                string[] texts = new string[PageCount];
                for (int i = 0; i < texts.Length; i++)
                {
                    texts[i] = GetPage(pd, i).Text;
                }
                if (kamoku is Kamoku k) new KamokuWindow(k, texts).Show();
            });
            RemoveNameCommand = new(name => {
                if (name is string n) NameList.Remove(n);
                NameIndex = NameList.Select((n, i) => (n.Length, i)).OrderByDescending(e => e.Length).FirstOrDefault().i;
            });
            AddNameCommand = new(name => {
                if (name is string n) NameList.Add(n);
                NameIndex = NameList.Count - 1;
            });
            RemoveCodeCommand = new(code => { if (code is string c) Numbering.Remove(c); });
            AddCodeCommand = new(code => { if (code is string c) Numbering.Add(c); });
        }

        [JsonIgnore]
        public ObservableCollection<string> NameList { get; } = new();

        private int nameIndex = 0;
        [JsonIgnore]
        public int NameIndex
        {
            get => nameIndex;
            set
            {
                nameIndex = value < 0 ? 0 : value;
                NotifyPropertyChanged(nameof(NameIndex));
                NotifyPropertyChanged(nameof(Name));
            }
        }
        public string Name => NameList[NameIndex];
        public string FileName { 
            get 
            {
                string n = Name;
                foreach(char c in Path.GetInvalidFileNameChars())
                {
                    n = n.Replace(c, '_');
                }
                return n;
            } 
        }


        public void ClearName()
        {
            NameList.Clear();
            NotifyPropertyChanged(nameof(NameList));
        }
        public void AddName(string name)
        {
            NameList.Add(name);
            if (Name.Length < name.Length)
            {
                NameIndex = NameList.Count - 1;
            }
            NotifyPropertyChanged(nameof(NameList));
        }

        public int StartPage;

        private int pageCount = 0;
        [JsonIgnore]
        public int PageCount
        {
            get => pageCount; set
            {
                pageCount = value;
                NotifyPropertyChanged(nameof(PageCount));
            }
        }

        private string period = string.Empty;
        public string Period
        {
            get => period; set
            {
                period = value;
                NotifyPropertyChanged(nameof(Period));
            }
        }

        public ObservableCollection<string> Numbering { get; set; } = new();

        [JsonIgnore]
        public CommandBase EditCommand { get; }
        [JsonIgnore]
        public CommandBase RemoveNameCommand { get; }
        [JsonIgnore]
        public CommandBase AddNameCommand { get; }
        [JsonIgnore]
        public CommandBase RemoveCodeCommand { get; }
        [JsonIgnore]
        public CommandBase AddCodeCommand { get; }

        public void OutputPDF(PdfDocument src, string dirName)
        {
            if (NameList.Count == 0) return;
            /*
            using(StreamWriter writer = new(Name + ".txt"))
            {
                writer.Write(src.GetPage(StartPage).Text);
            }
            */
            if (!Directory.Exists(dirName))
            {
                Directory.CreateDirectory(dirName);
            }
            using Stream stream = new FileStream(Path.Combine(dirName, FileName + ".pdf"), new FileStreamOptions() { Mode = FileMode.Create, Access = FileAccess.Write });
            using PdfDocumentBuilder doc = new(stream);
            for (int i = 0; i < PageCount; i++)
            {
                doc.AddPage(src, StartPage + i);
            }
        }

        public UglyToad.PdfPig.Content.Page GetPage(PdfDocument src, int index) => src.GetPage(StartPage + index);

        // 以下INotifyPropertyChanged
        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));
        public event PropertyChangedEventHandler? PropertyChanged;
    }

}
