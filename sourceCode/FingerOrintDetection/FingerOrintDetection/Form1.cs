using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
//using ImageProcessor.Imaging.Formats;
using libzkfpcsharp;


namespace FingerOrintDetection
{
    public partial class Form1 : Form
    {
        IntPtr mDevHandle = IntPtr.Zero;
        IntPtr mDBHandle = IntPtr.Zero;
        IntPtr FormHandle = IntPtr.Zero;
        bool bIsTimeToDie = false;
        bool IsRegister = false;
        bool bIdentify = true;
        byte[] FPBuffer;//chứa mã code base64 của fingerrint
        byte[] FPBufferTest;//chứa mã code base64 của fingerrint

        int RegisterCount = 0;
        const int REGISTER_FINGER_COUNT = 3;

        byte[][] RegTmps = new byte[3][];
        byte[] RegTmp = new byte[2048];
        byte[] CapTmp = new byte[2048];
        int cbCapTmp = 2048;
        int cbRegTmp = 0;
        int iFid = 1;//define Id cho fingerprint. băt đầu từ 1
        Thread captureThread = null;

        private int mfpWidth = 0;
        private int mfpHeight = 0;

        const int MESSAGE_CAPTURED_OK = 0x0400 + 6;

        [DllImport("user32.dll", EntryPoint = "SendMessageA")]
        public static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

        public Form1()
        {
            InitializeComponent();

            Load += Form1_Load;
        }

        /// <summary>
        /// Method Reading fingerprint.
        /// </summary>
        private void DoCapture()
        {
            while (!bIsTimeToDie)
            {
                cbCapTmp = 2048;
                int ret = zkfp2.AcquireFingerprint(mDevHandle, FPBuffer, CapTmp, ref cbCapTmp);
                if (ret == zkfp.ZKFP_ERR_OK)
                {
                    SendMessage(FormHandle, MESSAGE_CAPTURED_OK, IntPtr.Zero, IntPtr.Zero);
                }
                Thread.Sleep(200);
            }
        }

        protected override void DefWndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case MESSAGE_CAPTURED_OK:
                    {
                        MemoryStream ms = new MemoryStream();
                        BitmapFormat.GetBitmap(FPBuffer, mfpWidth, mfpHeight, ref ms);
                        Bitmap bmp = new Bitmap(ms);
                        this.picFPImg.Image = bmp;
                        //Show capture fingerprint encrypt base64
                        txtFingerprintCode.Text = $"Fingerprint:{Environment.NewLine}{zkfp2.BlobToBase64(CapTmp, cbCapTmp)}";

                        if (IsRegister)
                        {
                            int ret = zkfp.ZKFP_ERR_OK;
                            int fid = 0, score = 0;
                            ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
                            if (zkfp.ZKFP_ERR_OK == ret)
                            {
                                textRes.Text = "This finger was already register by " + fid + "!";
                                return;
                            }
                            if (RegisterCount > 0 && zkfp2.DBMatch(mDBHandle, CapTmp, RegTmps[RegisterCount - 1]) <= 0)
                            {
                                textRes.Text = "Please press the same finger 3 times for the enrollment";
                                return;
                            }
                            Array.Copy(CapTmp, RegTmps[RegisterCount], cbCapTmp);
                            String strBase64 = zkfp2.BlobToBase64(CapTmp, cbCapTmp);
                            byte[] blob = zkfp2.Base64ToBlob(strBase64);
                            RegisterCount++;

                            if (RegisterCount >= REGISTER_FINGER_COUNT)
                            {
                                RegisterCount = 0;
                                if (zkfp.ZKFP_ERR_OK == (ret = zkfp2.DBMerge(mDBHandle, RegTmps[0], RegTmps[1], RegTmps[2], RegTmp, ref cbRegTmp)) &&
                                       zkfp.ZKFP_ERR_OK == (ret = zkfp2.DBAdd(mDBHandle, iFid, RegTmp)))
                                {
                                    iFid++;
                                    textRes.Text = $"enroll succ";
                                    txtFingerprintCode.Text = $"Fingerprint:{Environment.NewLine}{zkfp2.BlobToBase64(RegTmp, cbRegTmp)}";

                                    //chuyển bytep[] thành HEX
                                    var hexCode= ConvertByteArray2Hex(RegTmp);
                                }
                                else
                                {
                                    textRes.Text = "enroll fail, error code=" + ret;
                                }
                                IsRegister = false;
                                return;
                            }
                            else
                            {
                                textRes.Text = "You need to press the " + (REGISTER_FINGER_COUNT - RegisterCount) + " times fingerprint";
                            }
                        }
                        else
                        {
                            if (cbRegTmp <= 0)
                            {
                                textRes.Text = "Please register your finger first!";
                                return;
                            }

                            if (bIdentify)//Xác thực vân tay
                            {
                                int ret = zkfp.ZKFP_ERR_OK;
                                int fid = 0, score = 0;
                                ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
                                if (zkfp.ZKFP_ERR_OK == ret)
                                {
                                    textRes.Text = "Identify succ, fid= " + fid + ",score=" + score + "!";
                                    return;
                                }
                                else
                                {
                                    textRes.Text = "Identify fail, ret= " + ret;
                                    return;
                                }
                            }
                            else//Kiểm chứng lại vân tay(verify)
                            {
                                int ret = zkfp2.DBMatch(mDBHandle, CapTmp, RegTmp);
                                if (0 < ret)
                                {
                                    textRes.Text = "Match finger succ, score=" + ret + "!";
                                    return;
                                }
                                else
                                {
                                    textRes.Text = "Match finger fail, ret= " + ret;
                                    return;
                                }
                            }
                        }
                    }
                    break;

                default:
                    base.DefWndProc(ref m);
                    break;
            }
        }

        private void CloseDevice()
        {
            if (IntPtr.Zero != mDevHandle)
            {
                bIsTimeToDie = true;
                Thread.Sleep(1000);
                captureThread.Join();
                zkfp2.CloseDevice(mDevHandle);
                mDevHandle = IntPtr.Zero;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            FormHandle = this.Handle;


        }

        private void bnIdentify_Click(object sender, EventArgs e)
        {
            IsRegister = false;
            cbRegTmp = cbCapTmp;

            if (!bIdentify)
            {
                bIdentify = true;
                textRes.Text = "Please press your finger!";
            }
        }

        private void bnClose_Click(object sender, EventArgs e)
        {
            CloseDevice();
            RegisterCount = 0;
            Thread.Sleep(1000);
            bnInit.Enabled = false;
            bnFree.Enabled = true;
            bnOpen.Enabled = true;
            bnClose.Enabled = false;
            bnEnroll.Enabled = false;
            bnVerify.Enabled = false;
            bnIdentify.Enabled = false;
        }

        private void bnVerify_Click(object sender, EventArgs e)
        {
            IsRegister = false;
            cbRegTmp = cbCapTmp;

            if (bIdentify)
            {
                bIdentify = false;
                textRes.Text = "Please press your finger!";
            }
        }

        /// <summary>
        /// add thêm fingerprint.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void bnEnroll_Click(object sender, EventArgs e)
        {
            if (!IsRegister)
            {
                IsRegister = true;
                RegisterCount = 0;
                cbRegTmp = 0;
                textRes.Text = "Please press your finger 3 times!";
            }
        }


        /// <summary>
        /// khởi tạo ban đầu.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void bnInit_Click(object sender, EventArgs e)
        {
            cmbIdx.Items.Clear();
            int ret = zkfperrdef.ZKFP_ERR_OK;
            if ((ret = zkfp2.Init()) == zkfperrdef.ZKFP_ERR_OK)
            {
                int nCount = zkfp2.GetDeviceCount();
                if (nCount > 0)
                {
                    for (int i = 0; i < nCount; i++)
                    {
                        cmbIdx.Items.Add(i.ToString());
                    }
                    cmbIdx.SelectedIndex = 0;
                    bnInit.Enabled = false;
                    bnFree.Enabled = true;
                    bnOpen.Enabled = true;
                }
                else
                {
                    zkfp2.Terminate();
                    MessageBox.Show("No device connected!");
                }
            }
            else
            {
                MessageBox.Show("Initialize fail, ret=" + ret + " !");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void bnFree_Click(object sender, EventArgs e)
        {
            zkfp2.Terminate();
            cbRegTmp = 0;
            bnInit.Enabled = true;
            bnFree.Enabled = false;
            bnOpen.Enabled = false;
            bnClose.Enabled = false;
            bnEnroll.Enabled = false;
            bnVerify.Enabled = false;
            bnIdentify.Enabled = false;
        }

        private void bnOpen_Click(object sender, EventArgs e)
        {
            int ret = zkfp.ZKFP_ERR_OK;
            if (IntPtr.Zero == (mDevHandle = zkfp2.OpenDevice(cmbIdx.SelectedIndex)))
            {
                MessageBox.Show("OpenDevice fail");
                return;
            }
            if (IntPtr.Zero == (mDBHandle = zkfp2.DBInit()))
            {
                MessageBox.Show("Init DB fail");
                zkfp2.CloseDevice(mDevHandle);
                mDevHandle = IntPtr.Zero;
                return;
            }
            bnInit.Enabled = false;
            bnFree.Enabled = true;
            bnOpen.Enabled = false;
            bnClose.Enabled = true;
            bnEnroll.Enabled = true;
            bnVerify.Enabled = true;
            bnIdentify.Enabled = true;
            btnClear.Enabled = true;
            btnAddFinger.Enabled = true;
            RegisterCount = 0;
            cbRegTmp = 0;
            iFid = 1;
            for (int i = 0; i < 3; i++)
            {
                RegTmps[i] = new byte[2048];
            }
            byte[] paramValue = new byte[4];
            int size = 4;
            zkfp2.GetParameters(mDevHandle, 1, paramValue, ref size);
            zkfp2.ByteArray2Int(paramValue, ref mfpWidth);

            size = 4;
            zkfp2.GetParameters(mDevHandle, 2, paramValue, ref size);
            zkfp2.ByteArray2Int(paramValue, ref mfpHeight);

            FPBuffer = new byte[mfpWidth * mfpHeight];
            FPBufferTest = new byte[mfpWidth * mfpHeight];

            captureThread = new Thread(new ThreadStart(DoCapture));
            captureThread.IsBackground = true;
            captureThread.Start();
            bIsTimeToDie = false;
            textRes.Text = "Open succ";
        }

        /// <summary>
        /// xóa hết fingerprint.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnClear_Click(object sender, EventArgs e)
        {
            //for (int i = 0; i < iFid; i++)
            //{
            //    zkfp2.DBDel(mDBHandle, i);
            //}

            if (zkfp2.DBClear(mDBHandle) == zkfp.ZKFP_ERR_OK)
            {
                iFid = 1;
                textRes.Text = "Cleared all fingerprint!";
            }
        }

        /// <summary>
        /// dung de test.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnAddFinger_Click(object sender, EventArgs e)
        {
            //Add vân tay Công lấy từ hệ thống của cty để thử
            //vân tay lưu trong DB của máy chấm công cty nó lư dạng số HEX của mảng Byte[], nên khi đọc về phải convert từ hex-->byte[]
            //rồi mới chuyển sang base64 nếu cần.
            FPBufferTest = ConvertHex2ByteArray("0x4AD75353323100000394970408050709CED000001B95690100000083B91D64944200FC0F4E008300779B41005600F20F4F006D947D0E99006F00C90EBC947F008D0F8A00420000999B008800830EF3008994F10F9B009700C80EBB94B2008E0FCA007A001F9BAD00CB008D0FAC00D594FF0F9600EA00E40E5894ED005C0F69003700069A8500FE002F0EF7000195510F19000C01960F71940E014E0EAB00CA013A9BCB001501340FA0002F95D10E94002F01950F42943401330FA600FE014D9B1F004A013A0FAD004C959F0F018AF20F6E826A11087A27FB6D81BBFBD4910870457A2EF83C90B61ECC0B6A0E86829B8382149CFA3D8A3D784480BEEE5A015F7F93EFF48C129A33020D7645035782FE06D7900313C90323704DFAD8A7C5FBABD078F739028FEB3558FD638B130E3C3C0B8ED8B5E2F413E5B8A0FF1FF4671DD8074CB66A1B1522E64843805190A3EAF61BD6FC6AE9B8738384EE352A9D8C06098ED2044665BAFAA294CE96FB17131D320AC2EB480BA3D17F6CCB52DFC2159121360102161C521B02830A436B58C15505C0C354C0515864C004C5732CE04E0700A33886065FC29C016A42FDC1339E0C03D154EDFE3E3857CA002CF5F5FFC14B3D53870903126A80776FFF07C59D6E922E590F002C6F283E4669543E07008771BFC0C1CD02009D730CC0D500DDE8926A8577C07705120375908CFEC3FFC04378C354C1C1A607009F510C56AC0A009797867905C2C3F606009F9A0F4A811103729D90C1526C690574C28501E1A39064C101FD7656FEC3520900B57690725689060046B669BDC008946FBBF7FEFCC13B41449A014EBCED3DFD38C05D6AC0FF060046BFA276FC910173C0FDFE3AC90072557BC3C0C367C03B980094CEC11AFF120022CA9D57C15AC09680FF076508946CD6F7FDFF313BFF34910192E7909F08C59AE983FCC0FEFD3014C5E3E9366BC26D9F59778F0A03C3ED62C28B84C3CF0059645D8477940B005DF01DA7FEFD3FFD0C00ABF1F96EFF2433340A0045FB8A248C96110035FE12FEFCAAFDFEFFFEC0FD84FE079489FE22211C1040022E682FFEFFFBFAFA3BFEF854FFFEFFFFFCFC3EFFFE6AFCFFFE1810DAC6B49A027BC287C270C39FC20B842C055370C272CD10309251C169900510B00B772104107A0C3DFDEC0313831053C10710ACD7342AAC0510741346FD3A061304325030F8051053334A6AFFFF0510403AF5C0B09011A23F4CFEC0C610ABAB48FE1810C8330583911683C1C2C2C2C004C1C2180E0070FF67C35AC2CB58C09FC3C8");            
            //zkfp2.DBMerge(mDBHandle, FPBufferTest, FPBufferTest, FPBufferTest, RegTmp, ref cbRegTmp);
            if (zkfp2.DBAdd(mDBHandle, iFid, FPBufferTest) == zkfp.ZKFP_ERR_OK)
            {
                iFid += 1;
            }

            FPBufferTest = ConvertHex2ByteArray("0x4D055353323100000446460408050709CED000001C47690100000084EB2499462100780E8D00E100F948CA002D00880F4C003146050E95004700BE0F67464B00FE0E74008E007848B3004E00090FBA004B46FA0EAB005200440FC6465F000D0F3F00A100654985006D00710F1F0073461D0F99009300BD0F124695005B0FE4005C002A49B3009A00270F0B00A5462F0F8500AD00940E5C46B3004A0FA60001004F48C300D3004B0F2700DB46400F2F00E400870FC746F200570FD9003E004A493100FF00C20F85000547380F71000801D20F8C461B01FC0F1B00E001B7493E003401AF0F4A0030478E0FA6003D012B0F3E464B019F0F817CAC7756B87884CDF8BA859382835656084508A58A080BA93C788219FE85844C076AB915838980E678258A9DB803901483798290FBEC3C940A15771506CC8F92B707670FE77F0B0F78BAB0E3FB06119AF177F311156728B60B03FFFE135F5DC4020A15D30920B361BDA7DF4502DD0BDB170E9E4B0AAE0B32FB520B23914B00CEFE02270F0072B2470D8DF7CEF96FED79C56B091FFB390A2F0C165FC0FA010FDF04008879C51713CD78F2F98A248159F2E09389B96FE4E7AF56DF06A2F3F314D81266B8927901A2A9975E79252436F437DBADF242ECA32D4CDA74B50D20FF010635209104007B0645590346810D7A70590CC5991AC677767AC204006421027B0F00CD2290C2045F7313C11200C73089AA66708687C0C0C107004833F978380C0085347ABDC0C58686C008008E39C543FA260400714E7AC3A90404C44F00C1FE0500724E0DB93E0300AF530F071204EE5480C0C0868005C2FA387B1400EB5A960484C41492C1666B0500006008863006003A6464AEC001463F6860C14912C580683C92C271C06B844B0B04CE6C00C032FBC18E1904C67074C3678B72A9FFC78792C1C09C030018781EB91B00F784A29388C1C486C0C471C1C0C20562C5281300959080C45EC2FB825BC3C1C2C1A5C30099D512FC30280094507A9BCE7DC191C4C0C60788C786C384C2C0C3935AC1934201179A5CC1C2C100E0DA26381100B69DE1FEFB77FEC027FEFDFAE912048D9C9E88C2C19704C2C58777C21E00F79E6C7F9487C36AC4C08BC105C2FB84FEC0C1C3FF9FCD00D6E42835470700CE603039731D0082B257C30795C180A6C3C3C1C2C406C6C785C2C2C0C3C2C1050404D3B527FAFD100091B64B8457C4FFC2C19D018E11465BB64C78819301C38986C3C0C4C1040054B74DD005008CB9509FE700A1F9AAC6CAC5C4C206C5C281C5C4C3C2C3C400C6C782C3C1C3C1C2C401C6C583C3C1C20300A90243F95E01F3CBB164C3BC80C4DF887E7C0A00C512462DBBFFFBFFFA030027E444B90710DF104CFD383803462AE74370A11EC5F0E7FB827AC09786C2497CC534FEC2920600C33256C4BAFFFC0710DB028CFEF9750F103D04409207C268D2C30F1043043A07C19686C293C0C1C307D5041676C26BC204101FEC24584111072B24C169060314442D34C1041043FD1E804511104022C205D5AF643B5400");
            //zkfp2.DBMerge(mDBHandle, FPBufferTest, FPBufferTest, FPBufferTest, RegTmp, ref cbRegTmp);
            if (zkfp2.DBAdd(mDBHandle, iFid, FPBufferTest) == zkfp.ZKFP_ERR_OK)
            {
                iFid += 1;
            }

            var b2h=ConvertByteArray2Hex(FPBufferTest);

            cbRegTmp = 100;
        }

        private byte[] ConvertHex2ByteArray(string hexString)
        {
            // Chuỗi hex cần chuyển đổi
            //string hexString = "4AD75353323100000394970408050709CED000001B95690100000083B91D64944200FC0F4E008300779B41005600F20F4F006D947D0E99006F00C90EBC947F008D0F8A00420000999B008800830EF3008994F10F9B009700C80EBB94B2008E0FCA007A001F9BAD00CB008D0FAC00D594FF0F9600EA00E40E5894ED005C0F69003700069A8500FE002F0EF7000195510F19000C01960F71940E014E0EAB00CA013A9BCB001501340FA0002F95D10E94002F01950F42943401330FA600FE014D9B1F004A013A0FAD004C959F0F018AF20F6E826A11087A27FB6D81BBFBD4910870457A2EF83C90B61ECC0B6A0E86829B8382149CFA3D8A3D784480BEEE5A015F7F93EFF48C129A33020D7645035782FE06D7900313C90323704DFAD8A7C5FBABD078F739028FEB3558FD638B130E3C3C0B8ED8B5E2F413E5B8A0FF1FF4671DD8074CB66A1B1522E64843805190A3EAF61BD6FC6AE9B8738384EE352A9D8C06098ED2044665BAFAA294CE96FB17131D320AC2EB480BA3D17F6CCB52DFC2159121360102161C521B02830A436B58C15505C0C354C0515864C004C5732CE04E0700A33886065FC29C016A42FDC1339E0C03D154EDFE3E3857CA002CF5F5FFC14B3D53870903126A80776FFF07C59D6E922E590F002C6F283E4669543E07008771BFC0C1CD02009D730CC0D500DDE8926A8577C07705120375908CFEC3FFC04378C354C1C1A607009F510C56AC0A009797867905C2C3F606009F9A0F4A811103729D90C1526C690574C28501E1A39064C101FD7656FEC3520900B57690725689060046B669BDC008946FBBF7FEFCC13B41449A014EBCED3DFD38C05D6AC0FF060046BFA276FC910173C0FDFE3AC90072557BC3C0C367C03B980094CEC11AFF120022CA9D57C15AC09680FF076508946CD6F7FDFF313BFF34910192E7909F08C59AE983FCC0FEFD3014C5E3E9366BC26D9F59778F0A03C3ED62C28B84C3CF0059645D8477940B005DF01DA7FEFD3FFD0C00ABF1F96EFF2433340A0045FB8A248C96110035FE12FEFCAAFDFEFFFEC0FD84FE079489FE22211C1040022E682FFEFFFBFAFA3BFEF854FFFEFFFFFCFC3EFFFE6AFCFFFE1810DAC6B49A027BC287C270C39FC20B842C055370C272CD10309251C169900510B00B772104107A0C3DFDEC0313831053C10710ACD7342AAC0510741346FD3A061304325030F8051053334A6AFFFF0510403AF5C0B09011A23F4CFEC0C610ABAB48FE1810C8330583911683C1C2C2C2C004C1C2180E0070FF67C35AC2CB58C09FC3C8";

            // Bỏ "0x" nếu có trong chuỗi
            if (hexString.StartsWith("0x"))
            {
                hexString = hexString.Substring(2);
            }

            // Chuyển chuỗi hex sang mảng byte
            byte[] bytes = new byte[hexString.Length / 2];
            for (int i = 0; i < hexString.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
            }

            return bytes;
        }

        private string ConvertByteArray2Hex(byte[] bytes)
        {
            //byte[] bytes = { 0x4A, 0xD7, 0x53, 0x53, 0x23, 0x31, 0x00, 0x00 };

            StringBuilder hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                hex.AppendFormat("{0:X2}", b);
            }

            string hexString = hex.ToString();
           return hexString;
        }
    }
}
