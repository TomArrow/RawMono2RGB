using BitMiracle.LibTiff.Classic;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Shapes = System.Windows.Shapes;
using Forms = System.Windows.Forms;

using System.Drawing;
using Imaging = System.Drawing.Imaging;
using Orientation = BitMiracle.LibTiff.Classic.Orientation;
using System.Runtime.InteropServices;
using System.Threading;
using ImageMagick;
using System.Text.RegularExpressions;

namespace RawMono2RGB
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window,  INotifyPropertyChanged
    {
        
        private const TiffTag TIFFTAG_CFAREPEATPATTERNDIM = (TiffTag)33421;
        private const TiffTag TIFFTAG_CFAPATTERN = (TiffTag)33422;

        //private static Tiff.TiffExtendProc m_parentExtender;
        private BackgroundWorker worker = new BackgroundWorker();

        string sourceFolder = null;
        string targetFolder = null;
        string[] filesInSourceFolder = null;
        private int currentProgress;
        private string currentStatus;
        private static int _counterTotal = 0;
        private static int _counterDone = 0;
        private static int _counterSkippedExisting = 0;
        private static int _counterSkippedRange = 0;
        private static int _totalFiles = 0;

        // Declare the event
        public event PropertyChangedEventHandler PropertyChanged;


        // Create the OnPropertyChanged method to raise the event
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
        }


        private enum FORMAT { MONO16, MONO12p };

        private FORMAT getInputFormat()
        {
            return this.Dispatcher.Invoke(() =>
            {
                FORMAT inputFormat = FORMAT.MONO16;

                if ((bool)formatRadio_mono16.IsChecked)
                {
                    inputFormat = FORMAT.MONO16;
                }
                else if ((bool)formatRadio_mono12p.IsChecked)
                {
                    inputFormat = FORMAT.MONO12p;
                }
                return inputFormat;
            });
        }

        private byte[] convert12pto16bit(byte[] input)
        {
            long inputlength = input.Length * 8;
            long outputLength = inputlength / 12 * 16;
            long inputlengthBytes = inputlength / 8;
            long outputLengthBytes = outputLength / 8;

            byte[] output = new byte[outputLengthBytes];

            // For each 3 bytes in input, we write 4 bytes in output
            for (long i = 0, o = 0; i < inputlengthBytes; i += 3, o += 4)
            {

                output[o + 1] = (byte)((input[i] & 0b1111_0000) >> 4 | ((input[i + 1] & 0b0000_1111) << 4));
                output[o] = (byte)((input[i] & 0b0000_1111) << 4);
                output[o + 3] = (byte)input[i + 2];
                output[o + 2] = (byte)((input[i + 1] & 0b1111_0000));
            }

            return output;
        }


        private enum TARGETFORMAT { TIF,EXR};
        

        private void ProcessRAW( string[] srcRGBTriplet,string targetFilename, TARGETFORMAT targetFormat, FORMAT inputFormat,int maxThreads)
        {

            byte[] buffR = File.ReadAllBytes(srcRGBTriplet[0]);
            byte[] buffG = File.ReadAllBytes(srcRGBTriplet[1]);
            byte[] buffB = File.ReadAllBytes(srcRGBTriplet[2]);

            if(inputFormat == FORMAT.MONO12p)
            {
                buffR = convert12pto16bit(buffR);
                buffG = convert12pto16bit(buffG);
                buffB = convert12pto16bit(buffB);
            }


            int width = 4096;
            int height = 3000;

            this.Dispatcher.Invoke(() =>
            {
                width = 1;
                int.TryParse(rawWidth.Text,out width);
                height = 1;
                int.TryParse(rawHeight.Text,out height);
            });


            // Interleave
            int pixelCount = width * height;
            int totalLength = width * height * 3;
            byte[] buff = new byte[totalLength*2];


            if (buffR.Count() < pixelCount * 2)
            {
                this.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Red file too short: "+ srcRGBTriplet[0]);
                });
                return;
            }
            if (buffG.Count() < pixelCount * 2)
            {
                this.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Green file too short: " + srcRGBTriplet[1]);
                });
                return;
            }
            if (buffB.Count() < pixelCount * 2)
            {
                this.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Blue file too short: " + srcRGBTriplet[2]);
                });
                return;
            }

            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                /*
                // BGR
                buff[pixelIndex * 3 * 2] = buffB[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 + 1] = buffB[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 +4] = buffR[pixelIndex*2];
                buff[pixelIndex * 3 * 2 +5] = buffR[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 +2] = buffG[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 +3] = buffG[pixelIndex * 2 + 1];
                */
                // RGB
                buff[pixelIndex * 3 * 2] = buffR[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 + 1] = buffR[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 + 2] = buffG[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 + 3] = buffG[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 + 4] = buffB[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 + 5] = buffB[pixelIndex * 2 + 1];
                
            }
            


            string fileName = targetFilename;

            if(targetFormat == TARGETFORMAT.EXR)
            {
                ResourceLimits.Thread = (ulong)maxThreads;

                MagickReadSettings settings = new MagickReadSettings();
                settings.Width = width;
                settings.Height = height;
                settings.Format = MagickFormat.Rgb; // Correction, this is actually right, I had flipped RGB to BGR elsewhere in the code before. Fixed now.
                using (var image = new MagickImage(buff, settings))
                {
                    //ExifProfile profile = new ExifProfile();
                    //profile.SetValue(ExifTag.UserComment, Encoding.ASCII.GetBytes(srcRGBTriplet[0] + "," + srcRGBTriplet[1] + "," + srcRGBTriplet[2]));
                    //image.SetProfile(profile);
                    image.Format = MagickFormat.Exr;
                    image.Settings.Compression = CompressionMethod.Piz;
                    image.Write(fileName);
                }
            }
            else if (targetFormat == TARGETFORMAT.TIF)
            {

                using (Tiff output = Tiff.Open(fileName, "w"))
                {

                    output.SetField(TiffTag.SUBFILETYPE, 0);
                    //output.SetField(TiffTag.ORIGINALRAWFILENAME, srcRGBTriplet[0]+","+srcRGBTriplet[1]+","+srcRGBTriplet[2]);
                    output.SetField(TiffTag.IMAGEWIDTH, width);
                    output.SetField(TiffTag.IMAGELENGTH, height);
                    output.SetField(TiffTag.SAMPLESPERPIXEL, 3);
                    output.SetField(TiffTag.BITSPERSAMPLE, 16);
                    output.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
                    output.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
                    output.SetField(TiffTag.FILLORDER, FillOrder.MSB2LSB);
                    output.SetField(TiffTag.COMPRESSION, Compression.DEFLATE); 

                    output.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);


                    output.WriteEncodedStrip(0, buff, width * height * 2 * 3);
                }
            }

        }

        static Regex rawFileFrameNumberRegexp = new Regex(@"[^\d](\d+)\.raw$", RegexOptions.IgnoreCase);

        static int OrderComparisonTwoRawFiles(string file1,string file2)
        {
            MatchCollection matches1 = rawFileFrameNumberRegexp.Matches(file1);
            MatchCollection matches2 = rawFileFrameNumberRegexp.Matches(file2);

            int value1 = 0;
            int value2 = 0;
            // Try cach just in case the regexp doesnt give proper results for some reason.
            try
            {
                value1 = int.Parse(matches1[0].Groups[1].Value);
                value2 = int.Parse(matches2[0].Groups[1].Value);
            } catch(Exception e)
            {
                // Blah
            }

            return value1.CompareTo(value2);
        }

        private void BtnLoadRAWFolder_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            bool? result = fbd.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                sourceFolder = fbd.SelectedPath;
                txtSrcFolder.Text = sourceFolder;
                if(targetFolder == null)
                {
                    targetFolder = sourceFolder;
                    txtTargetFolder.Text = targetFolder;
                }
                filesInSourceFolder = Directory.GetFiles(fbd.SelectedPath,"*.raw");

                //Sorting
                Array.Sort(filesInSourceFolder, OrderComparisonTwoRawFiles);


                currentImagNumber.Text = "1";
                int tripletCount = (int) Math.Floor(filesInSourceFolder.Count() / 3d);
                totalImageCount.Text = tripletCount.ToString();
                processToMax_txt.Text = "("+ tripletCount.ToString()+")";
                processFrom_txt.Text = "1";
                processTo_txt.Text = tripletCount.ToString();
                if (filesInSourceFolder.Count() % 3 != 0)
                {
                    MessageBox.Show("Warning: The count of .raw files in the folder is not a multiple of 3. Files may be discarded.");
                }
                slide_currentFile.Maximum = (Math.Floor(filesInSourceFolder.Count() / 3d));
                slide_currentFile.Minimum = 1;
                slide_currentFile.Value = 1;
                btnProcessFolder.IsEnabled = true;
                btnSaveOrderedList.IsEnabled = true;
            }
        }

        private void Slide_currentFile_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

            ReDrawPreview();
        }

        // Order of colors
        private byte[] getColorOrder()
        {
            return this.Dispatcher.Invoke(() =>
            {
                //0=Red, 1=Green,   2=Blue
                byte colorAval = 0;
                byte colorBval = 1;
                byte colorCval = 2;
                byte.TryParse(colorA.Text, out colorAval);
                byte.TryParse(colorB.Text, out colorBval);
                byte.TryParse(colorC.Text, out colorCval);
                byte[] colorOrder = { colorAval, colorBval, colorCval };
                return colorOrder;
            });
        }
        
        // Similar purpose as getColorOrder,
        // but the returned array's index is the 3 colors (0 red 1 green 2 blue) and the content is the position
        private byte[] getRGBPositions()
        {
            byte[] colorOrder = getColorOrder();
            byte[] RGBPositions = new byte[3];
            for(byte i =0;i<3;i++)
            {
                switch (colorOrder[i])
                {
                    case 0:
                        RGBPositions[0] = i;
                        break;
                    case 1:
                        RGBPositions[1] = i;
                        break;
                    case 2:
                        RGBPositions[2] = i;
                        break;
                    default:
                        // User entered some garbage!
                        break;
                }
            }
            return RGBPositions;
        }

        private void ReDrawPreview()
        {
            if(sourceFolder == null || filesInSourceFolder == null)
            {
                return; // Nothing to do here
            }

            int width = 1, height = 1;
            int.TryParse(rawWidth.Text, out width);
            int.TryParse(rawHeight.Text, out height);
           

            //bool doPreviewDebayer = (bool)previewDebayer.IsChecked;
            bool doPreviewGamma = (bool)previewGamma.IsChecked;

            int frameDelay = 0;
            int.TryParse(delay.Text, out frameDelay);
            byte[] RGBPositions = getRGBPositions();

            int sliderNumber = (int)slide_currentFile.Value;

            int baseIndex = (sliderNumber - 1)*3+ frameDelay;

            if((baseIndex + 2) > (filesInSourceFolder.Length -1))
            {
                MessageBox.Show("Triplet incomplete.");
                return;
            }

            int[] RGBIndizi = new int[3] { baseIndex+RGBPositions[0], baseIndex + RGBPositions[1], baseIndex + RGBPositions[2] };

            string[] RGBFiles = new string[3] { filesInSourceFolder[RGBIndizi[0]], filesInSourceFolder[RGBIndizi[1]] , filesInSourceFolder[RGBIndizi[2]] };

            redfile_txt.Text = RGBFiles[0]; 
            greenfile_txt.Text = RGBFiles[1]; 
            bluefile_txt.Text = RGBFiles[2];

            FORMAT inputFormat = getInputFormat();

            foreach (string file in RGBFiles)
            {
                if (!File.Exists(file))
                {
                    MessageBox.Show("weirdo error, apparently file " + file + " (no longer?) exists");
                    return;
                }
            }
            {
                int subsample = 8;
                int.TryParse(previewSubsample_txt.Text, out subsample);

                int newWidth = (int)Math.Ceiling((double)width / subsample);
                int newHeight = (int)Math.Ceiling((double)height / subsample);

                byte[] buffR = File.ReadAllBytes(RGBFiles[0]);
                byte[] buffG = File.ReadAllBytes(RGBFiles[1]);
                byte[] buffB = File.ReadAllBytes(RGBFiles[2]);
                int byteDepth = 2; // This is for the source
                int byteWidth = newWidth * 3; // This is for the preview. 3 means RGB
                int newStride = Helpers.getStride(byteWidth);


                if (inputFormat == FORMAT.MONO12p)
                {
                    buffR = convert12pto16bit(buffR);
                    buffG = convert12pto16bit(buffG);
                    buffB = convert12pto16bit(buffB);
                }

                byte[] newbytes;

                newbytes = Helpers.DrawPreview(buffR,buffG,buffB, newHeight, newWidth, height, width, newStride, byteDepth, subsample, doPreviewGamma);


                // Put preview into WPF image tag
                Bitmap manipulatedImage = new Bitmap(newWidth, newHeight, Imaging.PixelFormat.Format24bppRgb);
                Imaging.BitmapData pixelData = manipulatedImage.LockBits(new Rectangle(0, 0, newWidth, newHeight), Imaging.ImageLockMode.WriteOnly, Imaging.PixelFormat.Format24bppRgb);

                System.Runtime.InteropServices.Marshal.Copy(newbytes, 0, pixelData.Scan0, newbytes.Count());
                
                manipulatedImage.UnlockBits(pixelData);

                System.Runtime.InteropServices.Marshal.Copy(newbytes, 0, pixelData.Scan0, newbytes.Count());
                // Do the displaying
                mainPreview.Source = Helpers.BitmapToImageSource(manipulatedImage);
            }
        }

        private void PreviewGamma_Click(object sender, RoutedEventArgs e)
        {

            ReDrawPreview();
        }
        private void Color_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReDrawPreview();
        }

        private void BtnLoadTargetFolder_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            bool? result = fbd.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                targetFolder = fbd.SelectedPath;
                txtTargetFolder.Text = targetFolder;
                txtStatus.Text = "Target fodler set to " + targetFolder;
            }
        }

        private void BtnProcessFolder_Click(object sender, RoutedEventArgs e)
        {
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;
            worker.DoWork += worker_DoWork;
            worker.ProgressChanged += worker_ProgressChanged;
            worker.RunWorkerAsync();
        }

        public int CurrentProgress
        {
            get { return currentProgress; }
            private set
            {
                if (currentProgress != value)
                {
                    currentProgress = value;
                    OnPropertyChanged("CurrentProgress");                   
                }
            }
        }

        public string CurrentStatus
        {
            get { return currentStatus; }
            private set
            {
                if (currentStatus != value)
                {
                    currentStatus = value;
                    OnPropertyChanged("CurrentStatus");
                }
            }
        }

        private List<string[]> getTriplets()
        {

            List<string[]> completeTriplets = new List<string[]>();
            int frameDelay = 0;
            byte[] RGBPositions = new byte[1];
            this.Dispatcher.Invoke(() =>
            {
                frameDelay = 0;
                int.TryParse(delay.Text, out frameDelay);
                RGBPositions = getRGBPositions();
            });

            for (int baseIndex = frameDelay; baseIndex < filesInSourceFolder.Length; baseIndex += 3)
            {

                if ((baseIndex + 2) > (filesInSourceFolder.Length - 1))
                {
                    MessageBox.Show("Triplet incomplete. Skipping."); // TODO make these so they dont have to be clicked or sth
                    continue;
                }

                int[] RGBIndizi = new int[3] { baseIndex + RGBPositions[0], baseIndex + RGBPositions[1], baseIndex + RGBPositions[2] };

                string[] RGBFiles = new string[3] { filesInSourceFolder[RGBIndizi[0]], filesInSourceFolder[RGBIndizi[1]], filesInSourceFolder[RGBIndizi[2]] };

                completeTriplets.Add(RGBFiles);
            }
            return completeTriplets;

        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {

            //_totalFiles = filesInSourceFolder.Length;
            _totalFiles = (int)Math.Floor(filesInSourceFolder.Length/3d);

            List<string[]> completeTriplets = new List<string[]>();

            int frameDelay = 0;
            byte[] RGBPositions = new byte[1];
            bool IsTIFF = false;
            bool IsEXR = false;
            int maxThreads = Environment.ProcessorCount;
            string customOutputName = "";
            int leadingZeros = 0;
            bool overwriteExisting = false;
            this.Dispatcher.Invoke(() =>
            {
                frameDelay = 0;
                int.TryParse(delay.Text,out frameDelay);
                int.TryParse(outputNameLeadingZeros_txt.Text,out leadingZeros);
                int.TryParse(maxThreads_txt.Text,out maxThreads);
                RGBPositions = getRGBPositions();
                IsTIFF = (bool)formatTif.IsChecked;
                IsEXR = (bool)formatExr.IsChecked;
                customOutputName = outputNameBase_txt.Text;
                overwriteExisting = !(bool)overwrite_no.IsChecked && (bool)overwrite_yes.IsChecked;
            });

            if(maxThreads == 0)
            {
                maxThreads = Environment.ProcessorCount;
            }


            FORMAT inputFormat = getInputFormat();

            TARGETFORMAT targetFormat = TARGETFORMAT.EXR;

            if (IsEXR)
            {

                targetFormat = TARGETFORMAT.EXR;
            } else
            {
                targetFormat = TARGETFORMAT.TIF;
            }


            for (int baseIndex = frameDelay; baseIndex < filesInSourceFolder.Length; baseIndex += 3)
            {

                if ((baseIndex + 2) > (filesInSourceFolder.Length - 1))
                {
                    MessageBox.Show("Triplet incomplete. Skipping.");
                    continue;
                }

                int[] RGBIndizi = new int[3] { baseIndex + RGBPositions[0], baseIndex + RGBPositions[1], baseIndex + RGBPositions[2] };

                string[] RGBFiles = new string[3] { filesInSourceFolder[RGBIndizi[0]], filesInSourceFolder[RGBIndizi[1]], filesInSourceFolder[RGBIndizi[2]] };

                completeTriplets.Add(RGBFiles);
            }

            int processFrom = 1;
            int processTo = completeTriplets.Count(); 
            this.Dispatcher.Invoke(() =>
            {
                int.TryParse(processFrom_txt.Text, out processFrom);
                int.TryParse(processTo_txt.Text, out processTo);
            });

            // Index starting at 0, but GUI starts at 1
            processFrom--;
            processTo--;


            var countLock = new object();
            CurrentProgress = 0;

            _counterTotal = 0;
            _counterSkippedRange = 0;
            _counterDone = 0;
            _counterSkippedExisting = 0;

            Parallel.ForEach(completeTriplets,
                new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, (currentTriplet, loopState,index) =>
                    // foreach (string srcFileName in filesInSourceFolder)
                {

                    _counterTotal++;
                    var percentage = (double)_counterTotal / _totalFiles * 100.0;
                    if (worker.CancellationPending == true)
                    {
                        e.Cancel = true;
                        return;
                    }

                    if(index < processFrom || index > processTo)
                    {
                        // Skip this one.
                        _counterSkippedRange++;
                        lock (countLock) { worker?.ReportProgress((int)percentage); }
                        return;
                    }

                    string fileNameWithoutFolder = customOutputName == "" ? Path.GetFileNameWithoutExtension(currentTriplet[0]) : customOutputName + (leadingZeros == 0 ? index.ToString() : index.ToString("D"+leadingZeros.ToString()));

                    string fileNameWithoutExtension =
                        targetFolder + "\\" + fileNameWithoutFolder;
                    string fileName = fileNameWithoutExtension + (targetFormat == TARGETFORMAT.EXR ? ".exr" : "") + (targetFormat == TARGETFORMAT.TIF ? ".tif" : "");


                    if (File.Exists(fileName) && !overwriteExisting)
                    {
                        // Error: File already exists. No overwriting. Move on.
                        //continue;
                        _counterSkippedExisting++;
                        lock (countLock) { worker?.ReportProgress((int)percentage); }
                        return;
                    }

                    ProcessRAW(currentTriplet, fileName,targetFormat, inputFormat,maxThreads);
                    _counterDone++;
                    lock (countLock) { worker?.ReportProgress((int)percentage); }
                });

            txtStatus.Text = "Finished";
        }

        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // pbStatus.Value = e.ProgressPercentage;
            CurrentProgress = e.ProgressPercentage;
            txtStatus.Text = $"Processed {_counterTotal} out of {_totalFiles}. {_counterDone} successful, {_counterSkippedExisting} skipped (file exists), {_counterSkippedRange} skipped (not in selected range)";
           
            //this.Dispatcher.BeginInvoke(new Action(() => { pbStatus.Value = e.ProgressPercentage; }));
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            worker.CancelAsync();
        }

        private void MagnifierPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ReDrawPreview();
        }

        private void Amplify_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ReDrawPreview();
        }

        private void Delay_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReDrawPreview();
        }


        private void FormatRadio_Checked(object sender, RoutedEventArgs e)
        {
            ReDrawPreview();
        }

        private void PreviewSubsample_txt_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReDrawPreview();

        }

        private void BtnSaveOrderedList_Click(object sender, RoutedEventArgs e)
        {
            string output = "Ordered file list: \r\n";
            foreach(string filename in filesInSourceFolder)
            {
                output += filename + "\r\n";
            }

            List<string[]> completeTriplets = getTriplets();

            output += "\r\nList of all triplets:\r\n";

            foreach(string[] triplet in completeTriplets)
            {
                output += "Red: " + triplet[0]+"\r\n";
                output += "Green: " + triplet[1]+"\r\n";
                output += "Blue: " + triplet[2]+"\r\n";
                output += "--------------------------------------------------------------------------\r\n";
            }

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Text files (.txt)|*.txt";
            sfd.FileName = "list.txt";
            if (sfd.ShowDialog() == true)
            {
                File.WriteAllText(sfd.FileName, output);
            }
        }
    }
}
