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
            //Cong
            //FPBufferTest = zkfp2.Base64ToBlob("Sp9TUzIxAAAD3OEECAUHCc7QAAAj3WoBAABkgwEjYtzgAPxkkAB+AAm4OADZAGZkmAAX3QFkrADYAEtkjtyVAA" +
            //                "lksgBkAIm4DgDkAOVkSQBM3VFSPQBzALVko9xaAVAzhwCXAPm4XwD9APpk+ADd3O9kfACtAMJkTNwOAVxkfgDkASy4xADcABlcZgAz3ThRXQBJARdhWdxv" +
            //                "APlkEwD2AU+4awBYAPhkbwBU3AJPSQDuAKRkodzsAItkjwDIARy4jQCxAIVkuQCW3HtkJgCxACpkK9wnAU1kMwBAAPK4wAA2ATg6/QBZ3TBFpwBgAQAo8k" +
            //                "PIBwoRKIfUBjXeQXpBByZu1KRC1pPRRQb5dlOG9ky4h9IEhoKjBl1fk4OX+G/6jAJJUA8aZYEJezp5S1LIAvYb/gRL+fDdyP7loiGb/Yq6UZf1hYQ5jlD+N" +
            //                "oAAZt79LRMnMWEm7I7Wkh+TlwChNtsOHqK3hEqlYt8Ai/ILHQKSHiAv4AOCAPeJwwn809L/xXU5awD/yiJPbeZ2vfP/lY02LX+FgL1/wHBKqjP0wotfAHoB" +
            //                "nt4f82seZYG/+9zY0wCjdKfp+2LwOgbmVHZWdL6MsGvm3QMgOAHHFRm0BgB8RW1KlwQDSEt3awUAo5J9ZtoBZFtxwFLOAMK7h/95Tl0KxV1tKz89XAMAO7d" +
            //                "0wtgBPXdtPQ3FNIAx/j7/REwOxTaLKFL//v9dN8YALVV1wgcAkJLDN1nXAXmTfWnBBcP9pwIAkJgMwcIAeUV7am0RANlTkMOiwYFpWcASxduiTMFqwsH+ww" +
            //                "XAfR2ABgCTsgaHwAjcirSDeMLBosAF3JO5Bv5bC8WKv1rDwXjCWQfFkr3QREsJAKvWVW+LHRAAwNmTagTBih1dwXAJAKgfifwdkGAGADfconjD2QGw3BbAN" +
            //                "9YAAArd//1CRsD1wTsdEwDUuoxiBP/AtnWEwAsAYiX3/PT+OMAHADomZ8IdwGYHAE7sKP04IwgARu1nwb3Awx4KAGH59/47/y3hCABi/vr+O//9Iy8KEFoA" +
            //                "awWXdhwEEIsKkKDQENTYn2nBwWyJOolnHQYQkwwX/vb+C8xND2SSgAjVkhL8/jAgCBBP1mCRHIwKEGET+jn9ICA/ChB4HIkBxcAfwMPEwQQQRSAjwRIQ1Rug" +
            //                "wavCch7Dwm7C/8LCEH34LPz//DUF1WkkuMXEwQYQKe1QdB4EEHEsOiHBEG/vSDoEEKUz9S0HzBQ1SWAWEAglrh9ukMGdwMIH/8FVwRIQoVbDR5LHGMLDwJef" +
            //                "DtVZa2zB/CT5+Pnm/VGeARJDAQEBxWwC7kwAABJFUg==");
            ////zkfp2.DBMerge(mDBHandle, FPBufferTest, FPBufferTest, FPBufferTest, RegTmp, ref cbRegTmp);
            //if (zkfp2.DBAdd(mDBHandle, iFid, FPBufferTest) == zkfp.ZKFP_ERR_OK)
            //{
            //    iFid += 1;
            //}
            //FPBufferTest = zkfp2.Base64ToBlob("Sr1TUzIxAAAD/v8ECAUHCc7QAAAj/2oBAABZgyMklP4BAfRkmQDVAVKadwDeAOhkHQA3/0hL8QAVAfI4ff69AGJkyABhAA" +
            //    "+aswCcAHhkYwCR/v5kVgBAAYldA/9RATI0/wCzAI7AyAD7ACRkaAAc/0lenAC8AD5kjP60APNkYwDEAVuarABGAcpYaQBR/99QtwBZAb9Q8f5IATY19wCXAUbMjQC" +
            //    "AAPVkBQBK/gFksAATAZJkov7HAG5kpwD9Acug3gDGAItRrgAd/01kggBBAfxMVv7nAOFkBwHeATLe/gA7ATUimABM/0lHYABdAfov1v5IAIBkyaRvnLVQ7Fw1/kkK1w" +
            //    "lhd++SVhRCFtMWVvSn8n4GEXHnaGmDBJYKDp6CYHgakGN+nYgaDFP2ZvprDA4CxO4k/A4Dlg9bg4+Pe824InbsBPPV+kSDt3EYCAaYofg4ka54XwzWeP5yPe9m+uRR" +
            //    "ZBe4Z5gbn2G0sba1dPOYBCr/NRboEwkSY/ikgsoFgYHjD153d+2wCsZkwHd0fp3z6ACBg/HvW4JElJYX6vSOApsXPYy78vLvzohT+YB92P6a/KP9bAGK+5jycQjp/O" +
            //    "8S+gb39CYLgYHm91h9+7u7iQsg8AEB1Rq6BgC7Sb/Bwj7AAwDbSQk6DQOKafRc/cD9B/3Czw4A/nOTg71+wzzBwQ8A/XlKwcEBgnzAdAMBwHoUAQ0AkID0wP3+/LX+" +
            //    "DwETd5cFwXw6/8PAwMFszwCqcftAQEYJAGSTd4p/wQsAqZTFwEMBR/8LALKauMJ3PGvACQC6nMbALABFCwCynnSiwcM8wWAIAMyjw/47Pv4GAMSlgE7DC/7NqAz/Rk" +
            //    "HXARpilsB4iHL+AWrD7QAYpZrCZgSLdYHAVg0AkrUxPP2+Rv8HAHu8osHCPm4GAJ28+vX/Bv5/wGJkBABax3J8DgDcyIn/B4PAgFvBBgDkydM+/OoAFOegccAEjMGH" +
            //    "clkEAMP6SakJ/sr6Gvz/Kp4IEy9nzsHBwcFdEQOZ/9xAwCr/O//9AToFEF8DWgf+w/sRYwZWwU3IEMn2lsPFwcTEQMGW6gAR+6TBwATDajzCfP/Cwv1RCRNgDO38+v" +
            //    "ojOxMTEQ6pwcORxAXEh3mBCBDNEDHmQQnulxJXksLDAcbF9hGbFFPDjA3FB+6xFV6pExHBFa48wYjDhpPCr8AH7vEXOk8DEDAXMgAFEGshUHjAENTLSD0XEREdaMHD" +
            //    "fomCd8JyU8kQnry2/h31+/w6/Pj9EbFC18QD1bIgvfwZEQ5KsJDCf2yWiID/wYPGEPO1R8AYEORnDG2BP8LDwsPDwgfDwj/D/8HCwlKHABq9AAIBALQB40cD/wGoAT7" +
            //    "IAMUZRqw=");
            ////zkfp2.DBMerge(mDBHandle, FPBufferTest, FPBufferTest, FPBufferTest, RegTmp, ref cbRegTmp);
            //if (zkfp2.DBAdd(mDBHandle, iFid, FPBufferTest) == zkfp.ZKFP_ERR_OK)
            //{
            //    iFid += 1;
            //}

            //Add vân tay Công lấy từ hệ thống của cty để thử
            //vân tay lưu trong DB của máy chấm công cty nó lư dạng số HEX của mảng Byte[], nên khi đọc về phải convert từ hex-->byte[]
            //rồi mới chuyển sang base64 nếu cần.
            FPBufferTest = ConvertHex2ByteArray("0x4AD75353323100000394970408050709CED000001B95690100000083B91D64944200FC0F4E008300779B41005600F20F4F006D947D0E99006F00C90EBC947F008D0F8A00420000999B008800830EF3008994F10F9B009700C80EBB94B2008E0FCA007A001F9BAD00CB008D0FAC00D594FF0F9600EA00E40E5894ED005C0F69003700069A8500FE002F0EF7000195510F19000C01960F71940E014E0EAB00CA013A9BCB001501340FA0002F95D10E94002F01950F42943401330FA600FE014D9B1F004A013A0FAD004C959F0F018AF20F6E826A11087A27FB6D81BBFBD4910870457A2EF83C90B61ECC0B6A0E86829B8382149CFA3D8A3D784480BEEE5A015F7F93EFF48C129A33020D7645035782FE06D7900313C90323704DFAD8A7C5FBABD078F739028FEB3558FD638B130E3C3C0B8ED8B5E2F413E5B8A0FF1FF4671DD8074CB66A1B1522E64843805190A3EAF61BD6FC6AE9B8738384EE352A9D8C06098ED2044665BAFAA294CE96FB17131D320AC2EB480BA3D17F6CCB52DFC2159121360102161C521B02830A436B58C15505C0C354C0515864C004C5732CE04E0700A33886065FC29C016A42FDC1339E0C03D154EDFE3E3857CA002CF5F5FFC14B3D53870903126A80776FFF07C59D6E922E590F002C6F283E4669543E07008771BFC0C1CD02009D730CC0D500DDE8926A8577C07705120375908CFEC3FFC04378C354C1C1A607009F510C56AC0A009797867905C2C3F606009F9A0F4A811103729D90C1526C690574C28501E1A39064C101FD7656FEC3520900B57690725689060046B669BDC008946FBBF7FEFCC13B41449A014EBCED3DFD38C05D6AC0FF060046BFA276FC910173C0FDFE3AC90072557BC3C0C367C03B980094CEC11AFF120022CA9D57C15AC09680FF076508946CD6F7FDFF313BFF34910192E7909F08C59AE983FCC0FEFD3014C5E3E9366BC26D9F59778F0A03C3ED62C28B84C3CF0059645D8477940B005DF01DA7FEFD3FFD0C00ABF1F96EFF2433340A0045FB8A248C96110035FE12FEFCAAFDFEFFFEC0FD84FE079489FE22211C1040022E682FFEFFFBFAFA3BFEF854FFFEFFFFFCFC3EFFFE6AFCFFFE1810DAC6B49A027BC287C270C39FC20B842C055370C272CD10309251C169900510B00B772104107A0C3DFDEC0313831053C10710ACD7342AAC0510741346FD3A061304325030F8051053334A6AFFFF0510403AF5C0B09011A23F4CFEC0C610ABAB48FE1810C8330583911683C1C2C2C2C004C1C2180E0070FF67C35AC2CB58C09FC3C8");            
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
