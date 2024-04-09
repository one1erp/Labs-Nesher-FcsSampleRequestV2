using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using MSXML;
using LSExtensionWindowLib;
using LSSERVICEPROVIDERLib;
using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;
using Oracle.DataAccess.Client;
using DAL;
using Common;
using System.Net.Http;
using System.Net;
using System.Diagnostics;
using System.Runtime.Remoting.Messaging;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting;
using System.Text.RegularExpressions;
using OrderV2;
using System.Xml;
using System.Data.SqlTypes;







namespace FcsSampleRequestV2
{
    [ComVisible(true)]
    [ProgId("FcsSampleRequestV2.FcsSampleRequestV2Ctrl")]
    public partial class FcsSampleRequestV2Ctrl : UserControl//44444
    {


        #region variables

        public IExtensionWindowSite2 _ntlsSite;
        private IDataLayer _dal;
        public INautilusServiceProvider _sp;
        private INautilusProcessXML _processXml;
        private NautilusUser _ntlsUser;
        private INautilusDBConnection _ntlsCon;

        public string txtMsg = "";


        public OracleConnection oraCon;
        private OracleCommand cmd;


        public event Action<string> NewSdgCreated;
        public bool DEBUG;
        public string barcode;
        public string U_FCS_MSG_ID;
        public int sdgId;
        public string sdgName;
        public bool _return;
        public string statusMsg;
        private List<PhraseEntry> phrases = null;
        #endregion

        #region initial functions
        public FcsSampleRequestV2Ctrl(INautilusServiceProvider _sp, IExtensionWindowSite2 _ntlsSite, IDataLayer dal)
        {
            this._sp = _sp;
            this._ntlsSite = _ntlsSite;
            _dal = dal;
            InitializeComponent();
        }





        public OracleConnection GetConnection(INautilusDBConnection ntlsCon)
        {

            OracleConnection connection = null;

            if (ntlsCon != null)
            {


                // Initialize variables
                String roleCommand;
                // Try/Catch block
                try
                {



                    var _connectionString = ntlsCon.GetADOConnectionString();

                    var splited = _connectionString.Split(';');

                    var cs = "";

                    for (int i = 1; i < splited.Count(); i++)
                    {
                        cs += splited[i] + ';';
                    }

                    var username = ntlsCon.GetUsername();
                    if (string.IsNullOrEmpty(username))
                    {
                        var serverDetails = ntlsCon.GetServerDetails();
                        cs = "User Id=/;Data Source=" + serverDetails + ";";
                    }


                    //Create the connection
                    connection = new OracleConnection(cs);

                    // Open the connection
                    connection.Open();

                    // Get lims user password
                    string limsUserPassword = ntlsCon.GetLimsUserPwd();

                    // Set role lims user
                    if (limsUserPassword == "")
                    {
                        // LIMS_USER is not password protected
                        roleCommand = "set role lims_user";
                    }
                    else
                    {
                        // LIMS_USER is password protected.
                        roleCommand = "set role lims_user identified by " + limsUserPassword;
                    }

                    // set the Oracle user for this connecition
                    cmd = new OracleCommand(roleCommand, connection);

                    // Try/Catch block
                    try
                    {
                        // Execute the command
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception f)
                    {
                        // Throw the exception
                        throw new Exception("Inconsistent role Security : " + f.Message);
                    }

                    // Get the session id
                    var sessionId = _ntlsCon.GetSessionId();

                    // Connect to the same session
                    string sSql = string.Format("call lims.lims_env.connect_same_session({0})", sessionId);

                    // Build the command
                    cmd = new OracleCommand(sSql, connection);

                    // Execute the command
                    cmd.ExecuteNonQuery();

                }
                catch (Exception e)
                {
                    // Throw the exception
                    throw e;
                }

                // Return the connection
            }

            return connection;

        }

        public void Init()
        {

            if (!DEBUG)
            {
                _ntlsCon = _sp.QueryServiceProvider("DBConnection") as NautilusDBConnection;
                _processXml = Common.Utils.GetXmlProcessor(_sp);
                oraCon = GetConnection(_ntlsCon);
            }
            else
            {
                _processXml = null;
                oraCon = new OracleConnection("Data Source=MICROB;user id=lims_sys;password=lims_sys;");
                if (oraCon.State != ConnectionState.Open)
                {
                    oraCon.Open();
                }
                _dal = new MockDataLayer();
                _dal.Connect();
                phrases = _dal.GetPhraseByName("FCS Parameters").PhraseEntries.ToList();

            }

        }

        #endregion

        #region logical code
        public bool IsXmlFormat(string input)
        {
            string pattern = @"<[^<>]+>";
            return Regex.IsMatch(input, pattern);
        }

        private void SendRequest()
        {
            try
            {
                string fullUrl = "";
                string apiReqResult = "";

                lblMsg.Text = "Please wait...";

                Debugger.Launch();

                if (string.IsNullOrEmpty(textBoxBarcode.Text)) return;

                barcode = textBoxBarcode.Text;

                var currentSdg = _dal.GetSdgByExternalRef(barcode);

                if (currentSdg != null)
                {
                    lblMsg.Text = "דרישה קיימת במערכת";
                    return;                                     
                }

                string url = _dal.GetPhraseByName("UrlService_FCS").PhraseEntries
                    .FirstOrDefault(p => p.PhraseName == "FcsSampleRequest")?.PhraseDescription;

                fullUrl = url + barcode;

                //API request
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(fullUrl);
                request.Method = "POST";
                request.ContentLength = 0;
                using (WebResponse response = request.GetResponse())
                {
                    var a = response;
                    using (Stream responseStream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(responseStream))
                        {

                            apiReqResult = reader.ReadToEnd();

                        }
                    }
                }
                if (IsXmlFormat(apiReqResult))
                {
                    if (CreateSdg(apiReqResult))
                    {
                        NewSdgCreated(NEWSDGNAME);
                        lblMsg.Text = "דרישה נוצרה";
                    }
                    else
                    {
                        lblMsg.Text = errDesc;
                    }
                }
                else { lblMsg.Text = apiReqResult; }

            }
            catch (Exception ex)
            {
                lblMsg.Text = "שגיאה";
            }
        }

        string errDesc;
        private bool CreateSdg(string a1)
        {
            string _response = "";
            DOMDocument objDoc = new DOMDocument();
            objDoc.loadXML(a1);

            var objRes = new DOMDocument();

            try
            {
                _response = _processXml.ProcessXMLWithResponse(objDoc, objRes);
            }

            catch (Exception ex)
            {
                Logger.WriteLogFile(ex);

                errDesc = "xmlשגיאה בהמרה ל ";
                return false;

            }


            string suffix = ".xml";
            string savePath = @"c:\temp\";
            try
            {
                var directoryPath = savePath;
                if (directoryPath != null)
                {
                    string ut = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ss-fff");

                    objDoc.save(directoryPath + "Doc_" + (ut) + suffix);
                    savePath = (directoryPath + "Res_" + (ut) + suffix);
                    objRes.save(savePath);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLogFile(ex);
                errDesc = "שגיאה בשמירה";
                return false;
            }

            bool _success = _response.Length == 0;
            try
            {
                if (!_success && !string.IsNullOrEmpty(savePath))
                {
                    objRes.save(savePath + "_ERROR.xml");
                    errDesc = "שגיאה בתהליך יצירת או שמירת הדרישה";
                    return false;
                }

                else
                    objRes.save(savePath + suffix);
            }


            catch (Exception ex)
            {
                Logger.WriteLogFile(ex);
                errDesc = "שגיאה בשמירת מסמך התשובה";
                return false;
            }

            NEWSDGNAME = objRes.getElementsByTagName("NAME")[0].text;
            return true;

        }
        string NEWSDGNAME;
        #endregion

        #region buttons
        public void textBoxBarcode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SendRequest();
            }
        }
        private void btnExt_Click(object sender, EventArgs e)
        {
            ((Form)this.TopLevelControl).Close();
        }
        private void btnok_Click(object sender, EventArgs e)
        {
            SendRequest();
        }



        #endregion

        private void FcsSampleRequestV2Ctrl_Load(object sender, EventArgs e)
        {

        }
    }
}
