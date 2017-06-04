using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FlRockBand3.FormsApp
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            Load += Form_OnLoad;
        }

        private void Form_OnLoad(object sender, EventArgs e)
        {
            AllowDrop = true;
            DragEnter += Form_DragEnter;
            DragDrop += Form_DragDrop;
        }

        void Form_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 1)
                e.Effect = DragDropEffects.Copy;
        }

        void Form_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            filePathBox.Text = files[0];
        }

        private async void ConvertButtonClick(object sender, EventArgs e)
        {
            var midiPath = new FileInfo(filePathBox.Text);

            var convert = Task.Run(() =>
            {
                var fileDir = midiPath.Directory.FullName;
                var inFileName = Path.GetFileNameWithoutExtension(midiPath.Name);
                var outFilePath = Path.Combine(fileDir, Path.ChangeExtension(inFileName, "txt"));
                var fixedOutFilePath = Path.Combine(fileDir, Path.ChangeExtension(inFileName + "_clean", "mid"));
                var fixedOutFilePathTxt = Path.ChangeExtension(fixedOutFilePath, "txt");

                var dumper = new Dumper();
                using (var fs = new FileStream(outFilePath, FileMode.Create))
                    dumper.Dump(midiPath.FullName, fs);

                var fixer = new MidiFixer();
                fixer.Fix(midiPath.FullName, fixedOutFilePath);

                using (var fs = new FileStream(fixedOutFilePathTxt, FileMode.Create))
                    dumper.Dump(fixedOutFilePath, fs);
            });

            try
            {
                await convert;
                MessageBox.Show("Done", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (IOException ioe)
            {
                MessageBox.Show(ioe.Message, "File Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (FormatException fe)
            {
                MessageBox.Show(fe.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SelectPathButtonClick(object sender, EventArgs e)
        {
            var initialDir = @"C:\";
            if (!string.IsNullOrEmpty(filePathBox.Text))
                initialDir = filePathBox.Text;

            var openFileDialog1 = new OpenFileDialog
            {
                InitialDirectory = initialDir,
                Filter = @"MIDI files (*.mid)|*.mid|All files (*.*)|*.*",
                FilterIndex = 0,
                RestoreDirectory = false,
                Multiselect = false
            };

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                filePathBox.Text = openFileDialog1.FileName;
            }
        }
    }
}
