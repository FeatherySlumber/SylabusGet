using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Xml.Linq;
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
            NameList = new(nameList);

            EditCommand = new((kamoku) => {
                string[] texts = new string[PageCount];
                for (int i = 0; i < texts.Length; i++)
                {
                    texts[i] = GetPage(pd, i).Text;
                }
                if (kamoku is Kamoku k) new KamokuWindow(k, texts).Show();
            });
            RemoveNameCommand = new(name => {
                if (name is string n) nameList.Remove(n);
                NameIndex = nameList.Select((n, i) => (n.Length, i)).OrderByDescending(e => e.Length).FirstOrDefault().i;
            });
            AddNameCommand = new(name => {
                if (name is string n) nameList.Add(n);
                NameIndex = nameList.Count - 1;
            });
            RemoveCodeCommand = new(code => { if (code is string c) Numbering.Remove(c); });
            AddCodeCommand = new(code => { if (code is string c) Numbering.Add(c); });
        }

        [JsonIgnore]
        public ReadOnlyObservableCollection<string> NameList { get; }
        private readonly ObservableCollection<string> nameList = new();

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

        private string fileName = "";
        public string FileName { 
            get 
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = Name.GetAvailableFileName();
                }
                return fileName;
            } 
            set => fileName = value; }

        public void ClearName()
        {
            nameList.Clear();
            NotifyPropertyChanged(nameof(NameList));
        }
        public void AddName(string name)
        {
            nameList.Add(name);
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

    /// <summary>
    /// 
    /// </summary>
    public class Syllabus : ICollection<Kamoku>
    {
        public ReadOnlyObservableCollection<Kamoku> Kamokus { get; }
        private readonly ObservableCollection<Kamoku> kamokus = new();

        public Syllabus() 
        {
            Kamokus = new(kamokus);
        }

        public void Add(Kamoku item) => kamokus.Add(item);

        public void SetFileName(FileNameConfig config)
        {
            switch (config)
            {
                case FileNameConfig.Name:
                    for (int i = 0; i < kamokus.Count; i++)
                    {
                        kamokus[i].FileName = kamokus[i].Name.GetAvailableFileName();

                        int duplicateCount = 0;
                        for (int cnt = 0; cnt < i; cnt++)
                        {
                            if (kamokus[cnt].FileName == kamokus[i].FileName)
                            {
                                duplicateCount++;
                                kamokus[i].FileName += "_" + duplicateCount;
                                break;
                            }
                        }
                    }
                    break;
                case FileNameConfig.Number:
                    HashSet<int> hashs = new();
                    for (int i = 0; i < kamokus.Count; i++)
                    {
                        int hash;
                        int cnt = 0;
                        do
                        {
                            hash = kamokus[i].Name.GetHashCode() + cnt;
                            cnt++;
                        } while (!hashs.Add(hash));
                        kamokus[i].FileName = hash.ToString("X8");
                    }
                    break;
                case FileNameConfig.Index:
                    if(kamokus.Count != 0)
                    {
                        int digit = (int)Math.Log10(kamokus.Count) + 1;
                        for (int i = 0; i < kamokus.Count; i++)
                        {
                            kamokus[i].FileName = i.ToString("D" + digit.ToString());
                        }
                    }
                    break;
            }
        }

        public void JsonOutput(Stream stream)
        {
            JsonSerializer.Serialize(stream, kamokus, new JsonSerializerOptions()
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });
        }

        public void JsonOutput(Stream stream, FileNameConfig config)
        {
            SetFileName(config);
            JsonOutput(stream);
        }

        public void PdfOutput(PdfDocument pdf, string dirName)
        {
            foreach (Kamoku kamoku in kamokus) kamoku.OutputPDF(pdf, dirName);
        }

        public void PdfOutput(PdfDocument pdf, string dirName, FileNameConfig config)
        {
            SetFileName(config);
            PdfOutput(pdf, dirName);
        }

        public int Count => kamokus.Count;
        public bool IsReadOnly => false;
        public void Clear() => kamokus.Clear();
        public bool Contains(Kamoku item) => kamokus.Contains(item);
        public void CopyTo(Kamoku[] array, int arrayIndex) => kamokus.CopyTo(array, arrayIndex);
        public bool Remove(Kamoku item) => kamokus.Remove(item);
        public IEnumerator<Kamoku> GetEnumerator() => kamokus.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => kamokus.GetEnumerator();
    }

    public enum FileNameConfig
    {
        Name = 0,
        Number = 1,
        Index = 2
    }

    public static class Extend
    {
        public static string GetItemName(this FileNameConfig config) => config switch
        {
            FileNameConfig.Name => "科目名",
            FileNameConfig.Number => "数値",
            FileNameConfig.Index => "連番",
            _ => throw new NotImplementedException()
        };

        internal static string GetAvailableFileName(this string str)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) str = str.Replace(c, '_');
            return str;
        }
    }
}
