﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
using System.Xml;

namespace RemoteUpdate
{
    class Tasks
    {
        public static string Encrypt(string clearText)
        {
            if (clearText.Length == 0) { return clearText; }
            string EncryptionKey = System.Net.Dns.GetHostEntry("localhost").HostName + "RemoteUpdateByAIMA" + System.Security.Principal.WindowsIdentity.GetCurrent().Owner.ToString();
            byte[] clearBytes = Encoding.Unicode.GetBytes(clearText);
            using (Aes encryptor = Aes.Create())
            {
                Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(EncryptionKey, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(clearBytes, 0, clearBytes.Length);
                    }
                    clearText = Convert.ToBase64String(ms.ToArray());
                }
                pdb.Dispose();
            }
            return clearText;
        }
        public static string Decrypt(string cipherText)
        {
            string EncryptionKey = System.Net.Dns.GetHostEntry("localhost").HostName + "RemoteUpdateByAIMA" + System.Security.Principal.WindowsIdentity.GetCurrent().Owner.ToString();
            cipherText = cipherText.Replace(" ", "+");
            byte[] cipherBytes = Convert.FromBase64String(cipherText);
            using (Aes encryptor = Aes.Create())
            {
                Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(EncryptionKey, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(cipherBytes, 0, cipherBytes.Length);
                    }
                    cipherText = Encoding.Unicode.GetString(ms.ToArray());
                }
                pdb.Dispose();
            }
            return cipherText;
        }
        public static bool CheckPSConnection(int line)
        {
            var sessionState = InitialSessionState.CreateDefault();
            bool returnValue;
            using (var psRunspace = RunspaceFactory.CreateRunspace(sessionState))
            {
                psRunspace.Open();
                Pipeline pipeline = psRunspace.CreatePipeline();
                string tmpUsername = Global.TableRuntime.Rows[line]["Username"].ToString();
                string tmpPassword = Global.TableRuntime.Rows[line]["Password"].ToString();
                string tmpServername = Global.TableRuntime.Rows[line]["Servername"].ToString();

                if (tmpUsername.Length != 0 && tmpPassword.Length != 0)
                {
                    pipeline.Commands.AddScript("$pass = ConvertTo-SecureString -AsPlainText '" + tmpPassword + "' -Force;");
                    pipeline.Commands.AddScript("$Cred = New-Object System.Management.Automation.PSCredential -ArgumentList '" + tmpUsername + "',$pass;");
                    pipeline.Commands.AddScript("New-PSSession -Credential $Cred -ComputerName " + tmpServername + " -ConfigurationName VirtualAccount;");
                } else {
                    pipeline.Commands.AddScript("New-PSSession -ComputerName " + tmpServername + " -ConfigurationName VirtualAccount;");
                }
                try
                {
                    var exResults = pipeline.Invoke();
                    if(!pipeline.HadErrors)
                    {
                        WriteLogFile(0, "Connection to " + tmpServername.ToUpper(Global.cultures) + " works");
                        returnValue = true;
                    } else
                    {
                        WriteLogFile(1, "Connection to " + tmpServername.ToUpper(Global.cultures) + " is not working");
                        returnValue = false;
                    }
                } catch (PSSnapInException ee)
                {
                    WriteLogFile(2, "Connection to " + tmpServername.ToUpper(Global.cultures) + " failed: " + ee.Message);
                    returnValue = false;
                }
            }
            return returnValue;
        }
        public static bool CreatePSVirtualAccount(int line)
        {
            var sessionState = InitialSessionState.CreateDefault();
            using (var psRunspace = RunspaceFactory.CreateRunspace(sessionState))
            {
                string tmpServername = Global.TableRuntime.Rows[line]["Servername"].ToString();
                string tmpPSVirtualAccountName = Global.TableSettings.Rows[0]["PSVirtualAccountName"].ToString();
                string tmpUsername = Global.TableRuntime.Rows[line]["Username"].ToString();
                string tmpPassword = Global.TableRuntime.Rows[line]["Password"].ToString();

                psRunspace.Open();
                Pipeline pipeline = psRunspace.CreatePipeline();
                string tmpCredentials = "";
                if (tmpUsername.Length != 0 && tmpPassword.Length != 0)
                {
                    pipeline.Commands.AddScript("$pass = ConvertTo-SecureString -AsPlainText '" + Global.TableRuntime.Rows[line]["Password"].ToString() + "' -Force;");
                    pipeline.Commands.AddScript("$Cred = New-Object System.Management.Automation.PSCredential -ArgumentList '" + Global.TableRuntime.Rows[line]["Username"].ToString() + "',$pass;");
                    tmpCredentials = " -Credential $Cred";
                }
                pipeline.Commands.AddScript("Invoke-Command" + tmpCredentials + " -ComputerName '" + tmpServername + "' { if(!(Get-PackageProvider | Where { $_.Name -eq 'NuGet' -and $_.Version -lt 2.8.5.201})) { Install-PackageProvider -Name 'Nuget' -Force } };");
                pipeline.Commands.AddScript("Invoke-Command" + tmpCredentials + " -ComputerName '" + tmpServername + "' { if(!(Get-Module -ListAvailable -Name PSWindowsUpdate)) { Install-Module PSWindowsUpdate -Force } };");
                pipeline.Commands.AddScript("Invoke-Command" + tmpCredentials + " -ComputerName '" + tmpServername + "' { New-PSSessionConfigurationFile -RunAsVirtualAccount -Path .\\" + tmpPSVirtualAccountName + ".pssc };");
                pipeline.Commands.AddScript("Invoke-Command" + tmpCredentials + " -ComputerName '" + tmpServername + "' { Register-PSSessionConfiguration -Name '" + tmpPSVirtualAccountName + "' -Path .\\" + tmpPSVirtualAccountName + ".pssc -Force }");
                try
                {
                    var exResults = pipeline.Invoke();
                    if(pipeline.Error.Count > 1)
                    {
                        return false;
                    } else { 
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }
        public static void OpenPowerShell(int line, Grid GridMainWindow)
        {
            GridMainWindow.Children.OfType<Button>().Where(btn => btn.Name.Equals("ButtonTime_" + line.ToString(Global.cultures), StringComparison.Ordinal)).FirstOrDefault().Visibility = System.Windows.Visibility.Visible;
            GridMainWindow.Children.OfType<Button>().Where(btn => btn.Name.Equals("ButtonTime_" + line.ToString(Global.cultures), StringComparison.Ordinal)).FirstOrDefault().Content = DateTime.Now.ToString("HH:mm:ss", Global.cultures);
            GridMainWindow.Children.OfType<Button>().Where(btn => btn.Name.Equals("ButtonStart_" + line.ToString(Global.cultures), StringComparison.Ordinal)).FirstOrDefault().Visibility = System.Windows.Visibility.Hidden;
            

                ProcessStartInfo startInfo = new ProcessStartInfo(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
                startInfo.UseShellExecute = false;
                startInfo.EnvironmentVariables.Add("RedirectStandardOutput", "true");
                startInfo.EnvironmentVariables.Add("RedirectStandardError", "true");
                startInfo.EnvironmentVariables.Add("UseShellExecute", "false");
                startInfo.EnvironmentVariables.Add("CreateNoWindow", "false");
                if ((bool)GridMainWindow.Children.OfType<CheckBox>().Where(cb => cb.Name.Equals("CheckboxGUI_" + line.ToString(Global.cultures), StringComparison.Ordinal)).FirstOrDefault().IsChecked)
                {
                    startInfo.Arguments = "-noexit ";
                } else {
                    startInfo.Arguments = "-WindowStyle Hidden ";
                }
                // https://devblogs.microsoft.com/scripting/how-can-i-expand-the-width-of-the-windows-powershell-console/ entfern: $newsize.width = 120; 
                startInfo.Arguments += "$pshost = get-host; $pswindow = $pshost.ui.rawui; $newsize = $pswindow.buffersize; $newsize.height = 10; $pswindow.windowsize = $newsize;";
                startInfo.Arguments += "$host.ui.RawUI.WindowTitle = '" + Global.TableRuntime.Rows[line]["Servername"].ToString().ToUpper(Global.cultures) + "';";
                if (Global.TableRuntime.Rows[line]["Username"].ToString().Length != 0 && Global.TableRuntime.Rows[line]["Password"].ToString().Length != 0)
                {
                    startInfo.Arguments += "$pass = ConvertTo-SecureString -AsPlainText '" + Global.TableRuntime.Rows[line]["Password"].ToString() + "' -Force;";
                    startInfo.Arguments += "$Cred = New-Object System.Management.Automation.PSCredential -ArgumentList '" + Global.TableRuntime.Rows[line]["Username"].ToString() + "',$pass;";
                    startInfo.Arguments += "$session = New-PSSession -Credential $Cred -ConfigurationName '" + Global.TableSettings.Rows[0]["PSVirtualAccountName"] + "' -ComputerName " + Global.TableRuntime.Rows[line]["Servername"].ToString() + ";";
                }
                else
                {
                    startInfo.Arguments += "$session = New-PSSession -ConfigurationName '" + Global.TableSettings.Rows[0]["PSVirtualAccountName"] + "' -ComputerName " + Global.TableRuntime.Rows[line]["Servername"].ToString() + ";";
                }
                string WUArguments = "";
                if ((bool)GridMainWindow.Children.OfType<CheckBox>().Where(cb => cb.Name == "CheckboxAccept_" + line.ToString(Global.cultures)).FirstOrDefault().IsChecked)
                {
                    WUArguments += "-AcceptAll ";
                }
                if (!(bool)GridMainWindow.Children.OfType<CheckBox>().Where(cb => cb.Name == "CheckboxDrivers_" + line.ToString(Global.cultures)).FirstOrDefault().IsChecked)
                {
                    WUArguments += "-NotCategory Drivers ";
                }
                if ((bool)GridMainWindow.Children.OfType<CheckBox>().Where(cb => cb.Name == "CheckboxReboot_" + line.ToString(Global.cultures)).FirstOrDefault().IsChecked)
                {
                    WUArguments += "-AutoReboot ";
                }
                if ((bool)GridMainWindow.Children.OfType<CheckBox>().Where(cb => cb.Name == "CheckboxMail_" + line.ToString(Global.cultures)).FirstOrDefault().IsChecked)
                {
                    if ((Global.TableSettings.Rows[0]["SMTPServer"].ToString().Length != 0) && (Global.TableSettings.Rows[0]["SMTPPort"].ToString().Length != 0) && (Global.TableSettings.Rows[0]["MailFrom"].ToString().Length != 0) && (Global.TableSettings.Rows[0]["MailTo"].ToString().Length != 0))
                    {
                        WUArguments += "-SendReport –PSWUSettings @{ SmtpServer = '" + Global.TableSettings.Rows[0]["SMTPServer"].ToString() + "'; Port = " + Global.TableSettings.Rows[0]["SMTPPort"].ToString() + "; From = '" + Global.TableSettings.Rows[0]["MailFrom"].ToString() + "'; To = '" + Global.TableSettings.Rows[0]["MailTo"].ToString() + "' }";
                    }
                }
                startInfo.Arguments += "Invoke-Command $session { Install-WindowsUpdate -Verbose " + WUArguments + Global.TableSettings.Rows[0]["PSWUCommands"].ToString() + "}";
                Process test = Process.Start(startInfo);
        }
        public static string GetIPfromHostname(string Servername)
        {
            if (Servername.Length == 0) { return ""; }
            try
            {

                IPHostEntry hostEntry = Dns.GetHostEntry(Servername);
                if (IsLocalHost(hostEntry)) 
                {
                    WriteLogFile(0, "Returned IP 127.0.0.1 for " + Servername.ToUpper(Global.cultures) + " (localhost)", true);
                    return "127.0.0.1"; 
                }
                WriteLogFile(0, "Returned IP " + hostEntry.AddressList.FirstOrDefault().ToString() + " for " + Servername.ToUpper(Global.cultures), true); 
                return hostEntry.AddressList.FirstOrDefault().ToString();
            }
            catch (Exception ee)
            {
                WriteLogFile(1, "Could not get IP for Servername: " + Servername + ": " + ee.Message);
                return "";
            }
        }
        public static bool IsLocalHost(IPHostEntry ipAddress)
        {
            return Global.localHost.AddressList.Any(x => ipAddress.AddressList.Any(y => x.Equals(y)));
        }
        public static bool ReadXMLToTable(string xmlFilename, System.Data.DataTable loadTable)
        {
            if(File.Exists(xmlFilename))
            {
                try
                {
                    loadTable.ReadXmlSchema(xmlFilename);
                    loadTable.ReadXml(xmlFilename);
                    WriteLogFile(0, xmlFilename + " successfully imported");
                    return true;
                }
                catch (XmlException ee)
                {
                    WriteLogFile(2, xmlFilename + " not imported:" + ee.Message.ToString(Global.cultures)) ;
                    return false;
                }
            }
            WriteLogFile(1, xmlFilename + " not found");
            return false;
        }
        public static bool WriteTableToXML(System.Data.DataTable saveTable, string xmlFilename)
        {
            saveTable.WriteXml(xmlFilename, System.Data.XmlWriteMode.WriteSchema);
            WriteLogFile(0, xmlFilename + " successfully saved");
            return true;
        }
        public static bool CreateLogFile()
        {
            try
            {
                // Check Directory if there are write rights
                System.Security.AccessControl.DirectorySecurity ds = Directory.GetAccessControl(System.AppDomain.CurrentDomain.BaseDirectory);
                // Create Logfile
                using (Global.streamLogFile = File.AppendText(Global.strLogFile))
                {
                    Global.streamLogFile.WriteLine(System.DateTime.Now.ToString("yyyy.MM.dd_hhmmss", Global.cultures) + Global.stringTab + "INFO" + Global.stringTab + "Logfile created");
                }
                // Set Global variable that 
                Global.bDirectoryWritable = true;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Global.bDirectoryWritable = false;
                return false;
            }
        }
        public static void WriteLogFile(int iClassification, string strArgument)
        {
            WriteLogFile(iClassification, strArgument, false);
        }
        public static void WriteLogFile(int iClassification, string strArgument, bool bVerbose)
        {
            // Return if Global Verbose is false and the LogMessage is indicated as Verbose Log
            if(!Global.bVerboseLog && bVerbose) { return; }
            if(Global.bDirectoryWritable)
            {
                // Create Classification string
                string strClassification;
                if(iClassification == 0) {
                    strClassification = "INFO";
                } else if(iClassification == 1) {
                    strClassification = "WARNING";
                } else if(iClassification == 2) {
                    strClassification = "ERROR";
                } else {
                    strClassification = "UNKNOWN";
                }
                // Write Log Entry
                using (Global.streamLogFile = File.AppendText(Global.strLogFile))
                {
                    Global.streamLogFile.WriteLine(System.DateTime.Now.ToString("yyyy.MM.dd_hhmmss", Global.cultures) + Global.stringTab + strClassification + Global.stringTab + strArgument);
                }
            }
        }
    }
}
