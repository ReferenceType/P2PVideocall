using ServiceProvider.Services.Audio;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Shapes;

namespace Videocall.UserControls
{

    //public class SoundSliceData
    //{

    //    public SoundSliceData() { }
    //    public SoundSliceData(float[] sums)
    //    {
    //        V1 = (int)sums[0];
    //        V2 = (int)sums[1];
    //        V3 = (int)sums[2];
    //        V4 = (int)sums[3];
    //        V5 = (int)sums[4];
    //        V6 = (int)sums[5];
    //        V7 = (int)sums[6];
    //        V8 = (int)sums[7];
    //        V9 = (int)sums[8];
    //        V10 = (int)sums[9];
    //        V11 = (int)sums[10];
    //        V12 = (int)sums[11];
    //        V13 = (int)sums[12];
    //        V14 = (int)sums[13];
    //        V15 = (int)sums[14];
    //        V16 = (int)sums[15];
    //        V17 = (int)sums[16];
    //        V18 = (int)sums[17];
    //        V19 = (int)sums[18];
    //        V20 = (int)sums[19];
    //    }

    //    public int V1 { get; }
    //    public int V2 { get; }
    //    public int V3 { get; }
    //    public int V4 { get; }
    //    public int V5 { get; }
    //    public int V6 { get; }
    //    public int V7 { get; }
    //    public int V8 { get; }
    //    public int V9 { get; }
    //    public int V10 { get; }
    //    public int V11 { get; }
    //    public int V12 { get; }
    //    public int V13 { get; }
    //    public int V14 { get; }
    //    public int V15 { get; }
    //    public int V16 { get; }
    //    public int V17 { get; }
    //    public int V18 { get; }
    //    public int V19 { get; }
    //    public int V20 { get; }
    //}

    // Itemsource binding is too darn slow.. 

    //public struct IntBox
    //{
    //    public int Val
    //    {
    //        get; set;
    //    }
    //}
    //public class SoundSliceData : PropertyNotifyBase
    //{
    //    List<IntBox> soundValues = new List<IntBox>() { new IntBox { Val = 55 } };
    //    public List<IntBox> SoundValues
    //    {
    //        get => soundValues; set { soundValues = value; OnPropertyChanged(); }
    //    }
    //    public SoundSliceData()
    //    {
    //    }

    //    public SoundSliceData(double[] sums)
    //    {
    //        var sv = new List<IntBox>();
    //        foreach (var item in sums)
    //        {
    //            sv.Add(new IntBox { Val = (int)item });
    //        }
    //        SoundValues = sv;

    //    }

    //}

    public partial class SoundVisualizer : UserControl
    {
        public static readonly DependencyProperty SlideDataProp = DependencyProperty.Register("Data", typeof(SoundSliceData), typeof(SoundVisualizer), new FrameworkPropertyMetadata(new SoundSliceData()));

        public SoundSliceData Data
        {
            get { return (SoundSliceData)GetValue(SlideDataProp); }
            set { SetValue(SlideDataProp, value); }
        }


        public SoundVisualizer()
        {
            DataContext = this;
            InitializeComponent();
        }
    }

}
