/*
    CPBserver: Archery tournament web server
    Copyright (C) 2014  Mike Johnson

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Net;
using System.IO;
using System.Text;
using System.Globalization;
using System.Net.Sockets;
using System.Threading;

namespace CPBserver
{
    class Program
    {
        public static int TIMEOUT = 20;
        public static int RESULTPORT = 80;
        public static int UPDATEPORT = 4023;
        public static int ARCHERSPERTARGET = 4;
        public static string FilePath = ""; // "\\";
        public static IPAddress localipaddress;
        static Boolean shutdown = false;
        static Boolean resultsclosed = false;
        static int resultscount = 0;

        // print info
        static string tournament = "CPB Open Tournament";
        static string tournamentdate = "19/04/2015";
        static string venue = "";
        static string judges = "";
        static string paramount = "";
        static string patron = "";
        static string tournamentorganiser = "";
        static string timeofassembly = "";
        static string weather = "";

        // options
        static int resultsbyroundflag = 0;
        static int medalflag = 0;
        static int juniorflag = 0;
        static int bestflag = 0;
        static int handicapflag = 0;
        static int teamflag = 0;
        static int maxtargets = 40;
        static int scoresystem = 0;
        static int worldarchery = 0;

        // javascript pages
        static string allocate;
        static string byname;
        static string setuppage;
        static string hc2dozen;
        static string indexpage;
        static string movearcher;
        static string newarcher;
        static string printpage;
        static string printrun;
        static string printscore;
        static string results;
        static string rounds;
        static string scoreentry;
        static string scoresheet;
        static string updateentry;

        static byte[] logo;

        public const int MAXARCHERS = 99 * 4;
        public const string header = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=UTF-8\r\nConnection: close\r\n\r\n"
            + "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" /><title>CPB Server page</title>"
            + "<style type=\"text/css\">\r\n"
            + "h1{font-family:Helvetica,Arial,sans-serif;font-size:19pt;font-weight:normal;}"
            + "h2,h3{font-family:Helvetica,Arial,sans-serif;font-size:15pt;font-weight:normal;page-break-after:avoid;}"
            + "</style></head><body>\r\n";
        public const string fieldnames = "var TARGET = 0, SURNAME = 1, FORENAME = 2, CLUB = 3, TEAM = 4, BOW = 5;\r\n"
            + "var GENDER = 6, AGEGROUP=7, ROUND = 8, HANDICAP = 9, SCORE = 10, TIEBREAK1 = 11, TIEBREAK2 = 12;\r\n"
            + "var STATE = 13, ARROWCNT = 14, ARROWS = 15, CLASS = 16, AGB = 17, NOTES = 18, ENTRYREF = 19, PAID = 20;\r\n";

        public enum State { Free, Inuse, DNS, Retired, DQ };

        public class Archer
        {
            public string entryref;
            public string target;
            public string forename;
            public string surname;
            public string club;
            public string team;
            public string bowtype;
            public string round;
            public string gender;
            public string dateofbirth;
            public string agegroup;
            public float handicap;
            public int tiebreak1;
            public int tiebreak2;
            public int runningtotal;
            public int arrowcnt;
            public State state;
            public string arrows;
            public string classification;
            public string agbno;
            public string notes;
            public string paid;
            public float fee;
            public string email;
            public string address;
            public string phone;

            public void Clear()
            {
                entryref = "";
                forename = "";
                surname = "";
                club = "";
                team = "";
                bowtype = "";
                round = "NotSet";
                gender = "M";
                dateofbirth = "";
                agegroup = "";
                handicap = 100;
                tiebreak1 = 0;
                tiebreak2 = 0;
                runningtotal = 0;
                arrowcnt = 0;
                state = State.Free;
                arrows = "";
                classification = " ";
                agbno = "";
                notes = "";
                paid = "";
                fee = 0;
                email = "";
                address = "";
                phone = "";
            }

            public Archer()
            {
                Clear();
            }
        }

        public static Archer[] Archers = new Archer[MAXARCHERS];
        public static int MaxArchers = 0;
        static Object dataLock = new Object();

        // State object for reading client data asynchronously
        public class StateObject
        {
            // Client  socket.
            public Socket workSocket = null;
            // Size of receive buffer.
            public const int BufferSize = 1024;
            // Receive buffer.
            public byte[] buffer = new byte[BufferSize];
            // Received data string.
            public StringBuilder sb = new StringBuilder();
        }

        //**********************************************************************************************************************
        //
        //  Results thread
        //
        //**********************************************************************************************************************

        // Thread signal.
        public static ManualResetEvent resultsDone = new ManualResetEvent(false);

        public static void Results()
        {
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, RESULTPORT);
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(50);
                while (!shutdown)
                {
                    resultsDone.Reset();
                    // Console.WriteLine("Waiting for a results connection ...");
                    try
                    {
                        listener.BeginAccept(new AsyncCallback(ResultsAcceptCallback), listener);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                    resultsDone.WaitOne(Timeout.Infinite, shutdown);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            Console.WriteLine("Results service shutdown.");
        }

        public static void ResultsAcceptCallback(IAsyncResult ar)
        {
            try
            {
                // Signal the main thread to continue.
                resultsDone.Set();

                // Get the socket that handles the client request.
                Socket listener = (Socket)ar.AsyncState;
                Socket handler = listener.EndAccept(ar);

                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = handler;
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ResultsReadCallback), state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public static void ResultsReadCallback(IAsyncResult ar)
        {
            try
            {
                String content = String.Empty;

                // Retrieve the state object and the handler socket
                // from the asynchronous state object.
                StateObject state = (StateObject)ar.AsyncState;
                Socket handler = state.workSocket;
                IPAddress remoteip = ((IPEndPoint)handler.RemoteEndPoint).Address;

                // Read data from the client socket. 
                int bytesRead = handler.EndReceive(ar);

                if (bytesRead > 0)
                {
                    state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                    content = state.sb.ToString();
                    resultscount++;
                    Console.WriteLine("{0} Read {1} bytes from {2}:80. {3}", resultscount, content.Length,
                        remoteip, content.Substring(0, content.IndexOf('\n')));
                    if (resultsclosed)
                    {
                        string page = header + "<h2>Leaderboard Closed</h2>" + "</body></html>";
                        ResultsSend(handler, page);
                    }
                    else if (content.Contains("GET / ") || content.Contains("GET /index") || content.Contains("GET /scroll"))
                    {
                        // Send results back to client
                        string page = header + "<div id=\"page\"></div>";
                        if (content.Contains("GET /scroll"))
                            page += "<form id=\"data\" action=\"/scroll\" method=\"get\"></form>";
                        page += "<script type=\"text/javascript\">";
                        page += AllArchers(false) + flagstring() + infostring();
                        page += "var printing = false;\r\n";

                        if (content.Contains("GET /scroll"))
                        {
                            // scrolling
                            page += "var scrollstate = 0;"
                                 + "function pageScroll() { "
                                 + "if (scrollstate==0) { scrollstate=1; window.scrollTo(0,0); setTimeout('pageScroll()',3000); }"
                                 + "else if (window.pageYOffset+window.innerHeight>document.body.scrollHeight-10) "
                                 + "{ scrollstate=0; setTimeout('pageScroll()',3000); }"
                                 + "else { window.scrollBy(0,1); setTimeout('pageScroll()',10); }"
                                 + "};"
                                 + "pageScroll();";
                            // refresh page
                            page += "setTimeout(\"location.reload(true);\", 180000);";
                        }
                        else
                        {
                            page += "var scrollstate = -1;";
                        }
                        page += rounds + results;
                        ResultsSend(handler, page);
                    }
                    else
                    {
                        Console.WriteLine("Sending 404 not found.");
                        ResultsSend(handler, "HTTP/1.1 404 Not Found\r\n\r\n");
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        static string flagstring()
        {
            return "var medalflag=" + medalflag + ", juniorflag=" + juniorflag + ", teamflag=" + teamflag
                    + ", handicapflag=" + handicapflag + ", resultsbyroundflag=" + resultsbyroundflag
                    + ", bestflag=" + bestflag + ", resultsclosed=" + (resultsclosed ? 1 : 0)
                // + ", worldarchery=" + worldarchery
                    + ", maxtargets=" + maxtargets + ", scoresystem=" + scoresystem + ";\r\n";
        }

        static string infostring()
        {
            return "var tournament = \"" + tournament + "\", tournamentdate = \"" + tournamentdate + "\";\r\n"
                + "var venue = \"" + venue + "\", judges = \"" + judges + "\";\r\n"
                + "var paramount = \"" + paramount + "\", patron = \"" + patron + "\";\r\n"
                + "var tournamentorganiser = \"" + tournamentorganiser + "\", timeofassembly = \""
                + timeofassembly + "\";\r\n" + "var weather = \"" + weather + "\";\r\n";
        }

        private static void ResultsSend(Socket handler, String data)
        {
            try
            {
                // Convert the string data to byte data using ASCII encoding.
                byte[] byteData = Encoding.UTF8.GetBytes(data);

                // Begin sending the data to the remote device.
                handler.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(ResultsSendCallback), handler);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void ResultsSendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);
                Console.WriteLine("Sent results page {0} bytes.", bytesSent);

                handler.Shutdown(SocketShutdown.Both);
                handler.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static string AllArchers(bool paid)
        {
            string page = "var data = new Array();\r\n";
            lock (dataLock)
            {
                string t = Convert.ToString(maxtargets);
                if (t.Length == 1)
                    t = "0" + t;
                for (MaxArchers = MAXARCHERS; MaxArchers > 0; MaxArchers--)
                {
                    if (Archers[MaxArchers - 1].target.Substring(0, 2) == t)
                        break;
                    if (Archers[MaxArchers - 1].state != State.Free)
                        break;
                }
                for (int i = 0; i < MaxArchers; i++)
                {
                    t = Archers[i].target;
                    if (t == "" || t == " " || t == "0")
                        t = "&nbsp;";
                    page += ArcherData(i, "data[" + i + "]", false, paid);
                }
                page += "var maxdata =" + MaxArchers + ";\r\n";
            }
            return page + fieldnames;
        }

        //**********************************************************************************************************************
        //
        //  Update thread
        //
        //**********************************************************************************************************************

        // Thread signal.
        public static ManualResetEvent updateDone = new ManualResetEvent(false);

        public static void Updates()
        {
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, UPDATEPORT);
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(20);
                while (!shutdown)
                {
                    updateDone.Reset();
                    // Console.WriteLine("Waiting for an update connection...");
                    try
                    {
                        listener.BeginAccept(new AsyncCallback(UpdateAcceptCallback), listener);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                    updateDone.WaitOne(Timeout.Infinite, shutdown);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            Console.WriteLine("Update service shutdown.");
        }

        public static void UpdateAcceptCallback(IAsyncResult ar)
        {
            try
            {
                // Signal the main thread to continue.
                updateDone.Set();

                // Get the socket that handles the client request.
                Socket listener = (Socket)ar.AsyncState;
                Socket handler = listener.EndAccept(ar);

                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = handler;
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(UpdateReadCallback), state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public static void UpdateReadCallback(IAsyncResult ar)
        {
            String content = String.Empty;

            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            try
            {
                // Read data from the client socket. 
                int bytesRead = handler.EndReceive(ar);
                if (bytesRead > 0)
                {
                    // There  might be more data, so store the data received so far.
                    state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));

                    // Check for more data.
                    content = state.sb.ToString();
                    if (content.Contains("Content-Length:"))
                        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(UpdateReadCallback), state);
                    else if (!content.Contains("\r\n\r\n"))
                        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(UpdateReadCallback), state);
                    else
                    {
                        Console.WriteLine("Read {0} bytes from 4023. {1}", content.Length, content.Substring(0, content.IndexOf('\n')));
                        // Send data back to the client.
                        // string s = inputhandler(content);
                        // str = content.Substring(0, content.IndexOf('\n'));
                        if (content.Contains("GET /logo.gif"))
                            UpdateSend(handler, logo);
                        else
                        {
                            byte[] s = Encoding.UTF8.GetBytes(inputhandler(content));
                            UpdateSend(handler, s);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        // GET /updateentry.htm?oldtarget=10A&target=10A&archer=Anita+Baily&club=Leaves+Gre
        // en&bow=Recurve&gender=F&round=Hereford&score=887&hits=139&golds=32&dns=&retired=&dozen =12 HTTP/1.1
        private static string getparam(string s, string key)
        {
            if (s.IndexOf('\n') >= 0)
                s = s.Substring(0, s.IndexOf('\n'));
            int p = s.IndexOf('?'); // start of all params
            if (p < 0)
                return "NotFound";
            p = s.IndexOf(key + "=", p);
            if (p < 0)
                return "NotFound";
            p = p + key.Length + 1; // start of data
            int end = s.IndexOf(' ', p);
            int amp = s.IndexOf('&', p);
            if (amp < 0 || end < amp)
                s = s.Substring(p, end - p);
            else
                s = s.Substring(p, amp - p);
            s = s.Replace('+', ' ');
            while (true)
            {
                p = s.IndexOf('%');
                if (p < 0)
                    break;
                int n = Convert.ToInt32(s.Substring(p + 1, 2), 16);
                if (Convert.ToChar(n) == '\"')
                    s = s.Substring(0, p) + s.Substring(p + 3);
                else
                    s = s.Substring(0, p) + Convert.ToChar(n) + s.Substring(p + 3);
            }
            return s;
        }

        private static string inputhandler(string str)
        {
            str = str.Substring(0, str.IndexOf('\n'));
            if (str.Contains("GET /hc2dozen"))
            {
                string page = infostring() + "var data = new Array();\r\n";
                lock (dataLock)
                {
                    string t = Convert.ToString(maxtargets);
                    if (t.Length == 1)
                        t = "0" + t;
                    for (MaxArchers = MAXARCHERS; MaxArchers > 0; MaxArchers--)
                    {
                        if (Archers[MaxArchers - 1].target.Substring(0, 2) == t)
                            break;
                        if (Archers[MaxArchers - 1].state != State.Free)
                            break;
                    }
                    for (int i = 0; i < MaxArchers; i++)
                    {
                        page += ArcherData(i, "data[" + i + "]", false, false);
                    }
                    page += "var maxdata =" + MaxArchers + ";\r\n";
                }
                return header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n"
                    + rounds + fieldnames + page + hc2dozen;
            }
            if (str.Contains("GET /newhc2d"))
            {
                string list = getparam(str, "list");
                if (list == "" || list == "NotFound")
                    return setuppagestring();
                lock (dataLock)
                {
                    int i = 0, startp = 0, p = 0;
                    try
                    {
                        for (i = 0; i < MaxArchers; i++)
                        {
                            p = list.IndexOf(",", startp);
                            if (p == -1)
                                break;
                            Archers[i].handicap = Convert.ToSingle(list.Substring(startp, p - startp));
                            startp = p + 1;
                        }
                    }
                    catch { Console.WriteLine("Handicap {0} not a valid number {1}.", i, list.Substring(startp, p - startp)); }
                }
                return setuppagestring();
            }

            if (str.Contains("GET /clearscores"))
            {
                lock (dataLock)
                {
                    for (int i = 0; i < MAXARCHERS; i++)
                    {
                        if (Archers[i].state != State.Free)
                        {
                            Archers[i].arrowcnt = 0;
                            Archers[i].arrows = "";
                            Archers[i].tiebreak2 = 0;
                            Archers[i].tiebreak1 = 0;
                            Archers[i].runningtotal = 0;
                            Archers[i].state = State.Inuse;
                        }
                    }
                    timeout = TIMEOUT;
                }
            }
            if (str.Contains("GET /setupdata"))
            {
                lock (dataLock)
                {
                    tournament = getparam(str, "name");
                    tournamentdate = getparam(str, "date");
                    medalflag = getparam(str, "medals") == "on" ? 1 : 0;
                    bestflag = getparam(str, "best") == "on" ? 1 : 0;
                    juniorflag = getparam(str, "junior") == "on" ? 1 : 0;
                    teamflag = getparam(str, "team") == "on" ? 1 : 0;
                    handicapflag = getparam(str, "handicap") == "on" ? 1 : 0;
                    resultsbyroundflag = getparam(str, "round") == "on" ? 1 : 0;
                    /*
                    string s = getparam(str, "worldarchery");
                    if (s.Contains("World Archery Indoor"))
                        worldarchery = 2;
                    else if (s.Contains("World Archery Outdoor"))
                        worldarchery = 1;
                    else
                        worldarchery = 0;
                    */
                    try
                    {
                        maxtargets = Convert.ToInt32(getparam(str, "maxtargets"));
                    }
                    catch
                    {
                        maxtargets = 40;
                    };
                    string s = getparam(str, "scoresystem");
                    if (s.Contains("sheet"))
                        scoresystem = 0;
                    else if (s.Contains("dozen"))
                        scoresystem = 1;
                    else if (s.Contains("total"))
                        scoresystem = 2;
                    timeout = TIMEOUT;
                }
            }
            if (str.Contains("GET /setup") || str.Contains("GET /setupdata"))
            {
                // Console.WriteLine("Sending setup page.");
                return setuppagestring();
            }
            if (str.Contains("GET /printdata"))
            {
                tournament = getparam(str, "tournament");
                tournamentdate = getparam(str, "tournamentdate");
                venue = getparam(str, "venue");
                judges = getparam(str, "judges");
                paramount = getparam(str, "paramount");
                patron = getparam(str, "patron");
                tournamentorganiser = getparam(str, "tournamentorganiser");
                timeofassembly = getparam(str, "timeofassembly");
                weather = getparam(str, "weather");
                timeout = TIMEOUT;
            }
            if (str.Contains("GET /printpage") || str.Contains("GET /printdata"))
            {
                string results = header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n" + infostring();
                return results + flagstring() + rounds + printpage;
            }
            if (str.Contains("GET /printduplicate"))
            {
                string target = getparam(str, "target").ToUpper();
                if (target.Length == 2)
                    target = "0" + target;
                for (int i = 0; i < MAXARCHERS; i++)
                {
                    if (Archers[i].target == target && Archers[i].state != State.Free)
                    {
                        return header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n" + infostring()
                            + "var roundstr = \"\"; var printtype = 2;\r\n"
                            + flagstring() + rounds
                            + ArcherData(i, "var data", false, false)
                            + fieldnames + printscore;
                    }
                }
            }

            if (str.Contains("GET /printblankscore"))
            {
                string roundstr = getparam(str, "round");
                return header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n" + infostring()
                    + "var roundstr = \"" + roundstr + "\"; var printtype = 1;\r\n"
                    + flagstring() + rounds + printscore;

            }
            if (str.Contains("GET /printrun") || str.Contains("GET /printscore"))
            {
                string results = header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n";
                results += AllArchers(false) + infostring();
                // results += "var worldarchery = " + worldarchery + ";\r\n";
                results += "var roundstr; var printtype = 0;\r\n";
                if (str.Contains("GET /printrun"))
                    return results + rounds + printrun;
                else
                    return results + rounds + printscore;
            }
            if (str.Contains("GET /byname") || str.Contains("GET /byteam") || str.Contains("GET /bytarget")
                || str.Contains("GET /clearscores") || str.Contains("GET /byscore")
                || str.Contains("GET /printbyname") || str.Contains("GET /printbyteam") || str.Contains("GET /printbytarget")
                || str.Contains("GET /scorebyname") || str.Contains("GET /scorebyteam") || str.Contains("GET /scorebytarget")
                || str.Contains("GET /checkinbyname") || str.Contains("GET /entrybyname") || str.Contains("GET /entrybyclub")
                || str.Contains("GET /fullbyname") || str.Contains("GET /fullbyref"))
            {
                bool printing = str.Contains("print");
                bool entrylist = str.Contains("entry");
                bool checkin = str.Contains("checkin");
                bool fulllist = str.Contains("full");
                bool targetlist = str.Contains("clearscores") || !str.Contains("score");
                bool scoreby = str.Contains("scoreby") || str.Contains("byscore");
                bool sortbyname = str.Contains("byname");
                bool sortbyteam = str.Contains("byteam") || str.Contains("byclub");
                bool sortbyscore = str.Contains("byscore");
                bool sortbyref = str.Contains("byref");

                // Send results back to client
                string page = header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n";
                page += AllArchers(checkin) + flagstring() + infostring();
                page += "var printing = " + (printing ? "true" : "false") + ";\r\n";
                page += "var targetlist = " + (targetlist ? "true" : "false") + ";\r\n";
                page += "var entrylist = " + (entrylist ? "true" : "false") + ";\r\n";
                page += "var checkin = " + (checkin ? "true" : "false") + ";\r\n";
                page += "var fulllist = " + (fulllist ? "true" : "false") + ";\r\n";
                page += "var scoreby = " + (scoreby ? "true" : "false") + ";\r\n";
                page += "var sortbyscore = " + (sortbyscore ? "true" : "false") + ";\r\n";
                page += "var sortbyname = " + (sortbyname ? "true" : "false") + ";\r\n";
                page += "var sortbyteam = " + (sortbyteam ? "true" : "false") + ";\r\n";
                page += "var sortbyref = " + (sortbyref ? "true" : "false") + ";\r\n";
                return page + rounds + byname;
            }
            if (str.Contains("GET /printresults") || str.Contains("GET /results"))
            {
                bool printing = str.Contains("print");
                // Send results back to client
                string page = header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n";
                page += AllArchers(false) + flagstring() + infostring();
                page += "var printing = " + (printing ? "true" : "false") + ";\r\n";
                page += "var scrollstate = -1;";
                return page + rounds + results;
            }
            if (str.Contains("GET /newalloc"))
            {
                char[] letter = new char[6] { 'A', 'B', 'C', 'D', 'E', 'F' };
                string list = getparam(str, "list");
                if (list == "" || list == "NotFound")
                    return indexpagestring();
                // list is archer indexes or -1 for empty in target no order
                // * for an extra target E or F
                lock (dataLock)
                {
                    int startp = 0;
                    int t;
                    bool extra = false;
                    for (t = 0; t < MAXARCHERS; t++)
                        Archers[t].target = "";
                    t = 0;
                    int l = 9;
                    while (true)
                    {
                        int p = list.IndexOf(" ", startp);
                        if (p == -1)
                            break;
                        l++;
                        extra = list.Substring(startp, 1) == "*";
                        if (extra)
                            startp++;
                        else if (l >= 4)
                        {
                            l = 0;
                            t++;
                        }
                        int n = Convert.ToInt32(list.Substring(startp, p - startp));
                        if (n != -1)
                            Archers[n].target = t.ToString("0#") + letter[l];
                        else
                        {
                            for (n = 0; n < MAXARCHERS; n++)
                            {
                                if (Archers[n].state == State.Free && Archers[n].target == "")
                                {
                                    Archers[n].target = t.ToString("0#") + letter[l];
                                    Archers[n].round = "NotSet";
                                    break;
                                }
                            }
                        }
                        startp = p + 1;
                    }
                    for (int i = 0; i < MAXARCHERS; i++)
                    {
                        if (Archers[i].target == "")
                        {
                            l++;
                            if (l >= 4)
                            {
                                l = 0;
                                t++;
                            }
                            Archers[i].target = t.ToString("0#") + letter[l];
                            if (Archers[i].state == State.Free)
                                Archers[i].round = "NotSet";
                            t++;
                        }
                    }

                    timeout = TIMEOUT;
                }
                sortbytarget();
                insertfreetargets();
                sortbytarget();

                // fixup empty targets to the correct round
                lock (dataLock)
                {
                    for (int i = 0; i < MAXARCHERS; i++)
                    {
                        if (Archers[i].round == "NotSet")
                        {
                            for (int j = i - 6; j < i + 6; j++)
                            {
                                if (j < 0 || i == j || j >= MAXARCHERS
                                    || Archers[i].target.Substring(0, 2) != Archers[j].target.Substring(0, 2))
                                    continue;
                                if (Archers[j].round == "NotSet")
                                    continue;
                                Archers[i].round = Archers[j].round;
                                break;
                            }
                        }
                    }
                }
                checktargetnumbers();
                return setuppagestring();
            }
            if (str.Contains("GET /allocate"))
            {
                return header + "<div id=\"page\"></div><script type=\"text/javascript\">" + AllArchers(false) + flagstring()
                    + infostring() + allocate;
            }
            if (str.Contains("GET /move"))
            {
                // get old and new target numbers
                int fromi = -1, toi = -1;
                string fromtarget = getparam(str, "from").ToUpper();
                if (fromtarget.Length == 2)
                    fromtarget = "0" + fromtarget;
                string totarget = getparam(str, "to").ToUpper();
                if (totarget.Length == 2)
                    totarget = "0" + totarget;
                if (getparam(str, "send") == "send")
                {
                    lock (dataLock)
                    {
                        // find both targets
                        for (int i = 0; i < Archers.Length; i++)
                        {
                            if (Archers[i].target == fromtarget)
                                fromi = i;
                            if (Archers[i].target == totarget)
                                toi = i;
                            if (fromi != -1 && toi != -1)
                                break;
                        }
                        if (fromi != -1 && toi != -1)
                        {
                            // swap details between two target numbers, except for target no and possoibly round
                            Archer tmp = Archers[toi];
                            Archers[toi] = Archers[fromi];
                            Archers[fromi] = tmp;
                            string s = Archers[toi].target;
                            Archers[toi].target = Archers[fromi].target;
                            Archers[fromi].target = s;
                            if (Archers[toi].round == "NotSet")
                                Archers[toi].round = Archers[fromi].round;
                            if (Archers[fromi].round == "NotSet")
                                Archers[fromi].round = Archers[toi].round;
                            timeout = TIMEOUT;
                        }
                    }
                }

                string results = header + "<div id=\"page\"></div><script type=\"text/javascript\">";
                string r1 = ArcherDataStr(fromtarget, false, "var datafrom", false);
                string r2 = ArcherDataStr(totarget, false, "var datato", false);
                if (r1 != "" || r2 != "")
                    return results + rounds + r1 + r2 + fieldnames + flagstring() + infostring() + movearcher;
                else
                    return indexpagestring();
            }
            if (str.Contains("GET /updateentry"))
            {
                // GET /updateentry.htm?target=01A&archer=Rachel+Jones&club=Canterbury&bow=L&gender=F&round=Hereford&score=271&hits=79&golds=0&state=Inuse&dozen=12
                if (getparam(str, "send") == "send" || getparam(str, "send") == "next" || getparam(str, "send") == "score")
                {   // update data
                    UpdateArcher(str, UPDATE.All);
                }
                string target = getparam(str, "target").ToUpper();
                if (!"ABCDEF".Contains(target.Substring(target.Length - 1)))
                    target += "A";
                if (target.Length == 2)
                    target = "0" + target;
                if (getparam(str, "send") == "score")
                {
                    string pagefile = rounds;
                    if (scoresystem == 0)
                        pagefile += scoresheet;
                    else
                        pagefile += scoreentry;
                    return SendScorePage(pagefile, target, false, false);
                }
                return header + "<div id=\"page\"></div><script type=\"text/javascript\">"
                        + ArcherDataStr(target, getparam(str, "send") == "next", "var data", true) + fieldnames
                        + flagstring() + rounds + infostring() + updateentry;
            }
            if (str.Contains("GET /scoreentry"))
            {
                string pagefile = rounds;
                if (scoresystem == 0)
                    pagefile += scoresheet;
                else
                    pagefile += scoreentry;

                // check for data being returned
                bool skip = false;
                bool back = false;
                string send = getparam(str, "send");
                string target = getparam(str, "target").ToUpper();
                if (!"ABCDEF".Contains(target.Substring(target.Length - 1)))
                    target += "A";
                if (target.Length == 2)
                    target = "0" + target;
                string arrows = getparam(str, "arrowsend");
                if (arrows != "NotFound")
                {
                    // update scores
                    UpdateArcher(str, UPDATE.Score);
                }
                if (send == "cancel")
                    return indexpagestring();
                else if (send == "back")
                {
                    skip = true;
                    back = true;
                }
                else if (send == "next" || send == "data")
                    skip = true;
                return SendScorePage(pagefile, target, skip, back);
            }
            if (str.Contains("GET /rounds"))
            {
                // Console.WriteLine("Sending targets by round.");
                string list = "<table border=\"1\" style=\"border-collapse: collapse;\">";
                string rnd = "";
                string target = "";
                lock (dataLock)
                {
                    for (int i = 0; i < MAXARCHERS; i++)
                    {
                        if (Archers[i].state != State.Free)
                        {
                            if (rnd != Archers[i].round)
                            {
                                if (target == Archers[i].target.Substring(0, 2))
                                    list += "Target has different rounds.";
                                rnd = Archers[i].round;
                                list += "<tr><td valign=\"top\" width=\"160\">" + rnd + "<td width=\"300\">";
                                target = "";
                            }
                            if (target != Archers[i].target.Substring(0, 2))
                            {
                                target = Archers[i].target.Substring(0, 2);
                                list += target + ", ";
                            }
                        }
                    }
                }
                list += "</table>";
                return header + "<h2>" + tournament + "</h2><h3>Targets by Round</h3>"
                    + "<p><a href=\"/setup\">Set Up Page</a></p>"
                    + "<p>" + list + "</p></body></html>";
            }
            if (str.Contains("GET /clearall"))
            {
                lock (dataLock)
                {
                    for (int i = 0; i < MAXARCHERS; i++)
                        Archers[i].Clear();
                    timeout = TIMEOUT;
                }
            }
            if (str.Contains("GET /newarcher") || str.Contains("GET /clearall"))
            {
                string send = getparam(str, "send");
                string target = getparam(str, "target").ToUpper();
                if (!"ABCDEF".Contains(target.Substring(target.Length - 1)))
                    target += "A";
                if (target.Length == 2)
                    target = "0" + target;
                int targetindex;
                for (targetindex = 0; targetindex < MAXARCHERS; targetindex++)
                    if (Archers[targetindex].target == target)
                        break;
                if (send == "data")
                    UpdateArcher(str, UPDATE.Archer);

                // Console.WriteLine("Sending new archer.");
                string results = header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n";
                results += "var free = new Array(";
                string rnd = "qwerty";
                lock (dataLock)
                {
                    string t = Convert.ToString(maxtargets);
                    if (t.Length == 1)
                        t = "0" + t;
                    int max;
                    for (max = MAXARCHERS; max > 0; max--)
                    {
                        if (Archers[max - 1].target.Substring(0, 2) == t)
                            break;
                    }
                    for (int i = 0; i < max; i++)
                    {
                        if (Archers[i].state == State.Free)
                        {
                            string targetrnd = Archers[i].round;
                            if (targetrnd == "NotSet")
                            {
                                for (int j = i - 6; j < i + 6; j++)
                                {
                                    if (j < 0 || i == j || j > max
                                        || Archers[i].target.Substring(0, 2) != Archers[j].target.Substring(0, 2))
                                        continue;
                                    if (Archers[j].round == "NotSet")
                                        continue;
                                    targetrnd = Archers[j].round;
                                    break;
                                }
                            }

                            if (rnd != targetrnd)
                            {
                                if (rnd != "qwerty")
                                    results += "),\r\n";
                                rnd = targetrnd;
                                results += "new Array(\"" + rnd + "\"";
                            }
                            results += ", \"" + Archers[i].target + "\"";
                            if (targetindex == MAXARCHERS)
                            {
                                target = Archers[i].target;
                                targetindex = i;
                            }
                        }
                    }
                    results += ") );\r\n";
                }

                results += ArcherData(targetindex, "var data", true, false);
                return results + fieldnames + flagstring() + rounds + newarcher;
            }
            if (str.Contains("GET /leaderboard"))
            {
                resultsclosed = !resultsclosed;
                return indexpagestring();
            }
            if (str.Contains("GET /index"))
                return indexpagestring();
            if (str.Contains("GET /loadfile"))
            {
                string fn = getparam(str, "file");
                if (fn != "NotFound")
                {
                    ReadDataFile(fn);
                    sortbytarget();
                    insertfreetargets();
                    sortbytarget();
                    checktargetnumbers();
                    return setuppagestring();
                }
                string fp = FilePath;
                if (fp == "")
                    fp = System.IO.Directory.GetCurrentDirectory();
                string[] files = Directory.GetFiles(fp, "*.csv");
                string page = header + "<h2>Load File</h2><form>File: <select name=\"file\"/>";
                foreach (string s in files)
                    page += "<option>" + s + "</option>";
                page += "<input type=\"submit\" value=\"Load File\" formaction=\"/loadfile\"/></form></body></html>\r\n";
                return page;
            }
            if (str.Contains("GET /savefile"))
            {
                string fn = getparam(str, "file");
                if (fn != "NotFound")
                {
                    Console.WriteLine("Saving to " + FilePath + fn + ".csv");
                    SaveDataFile(FilePath + fn + ".csv");
                    return setuppagestring();
                }
                return header + "<h2>Save File</h2><form>\r\nFile: <input id=\"file\" name=\"file\"/>"
                    + "<input type=\"submit\" value=\"Save File\" formaction=\"/savefile\"/></form></body></html>\r\n";
            }
            Console.WriteLine("Sending 404 not found.");
            return "HTTP/1.1 404 Not Found\r\n\r\n";
        }

        static string indexpagestring()
        {
            return header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n"
                + infostring() + flagstring() + indexpage;
        }

        static string setuppagestring()
        {
            // Console.WriteLine("Sending setup page.");
            return header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n"
                + infostring() + flagstring() + setuppage;
        }

        enum UPDATE { All, Score, Archer };

        static void UpdateArcher(string str, UPDATE u)
        {
            string target = getparam(str, "target").ToUpper();
            string st = getparam(str, "state");
            if (target.Length == 2)
                target = "0" + target;
            lock (dataLock)
            {
                for (int i = 0; i < MAXARCHERS; i++)
                {
                    if (Archers[i].target == target)
                    {
                        if (u == UPDATE.All || u == UPDATE.Archer)
                        {
                            if (st == "Free")
                            {
                                Archers[i].Clear();
                                // make all space on that target the same round
                                bool allfree = true;
                                for (int j = -5; j < 6; j++)
                                {
                                    if (j != 0 && i + j >= 0 && i + j < MAXARCHERS
                                        && Archers[i + j].target.Substring(0, 2) == Archers[i].target.Substring(0, 2))
                                    {
                                        if (Archers[i + j].state != State.Free)
                                        {
                                            allfree = false;
                                            Archers[i].round = Archers[i + j].round;
                                            break;
                                        }
                                    }
                                }
                                if (allfree)
                                {
                                    for (int j = -5; j < 6; j++)
                                    {
                                        if (j != 0 && i + j >= 0 && i + j < MAXARCHERS
                                            && Archers[i + j].target.Substring(0, 2) == Archers[i].target.Substring(0, 2))
                                            Archers[i + j].round = "NotSet";
                                    }
                                }
                                break;
                            }
                            else
                            {
                                if (st == "DNS")
                                    Archers[i].state = State.DNS;
                                else if (st == "DQ")
                                    Archers[i].state = State.DQ;
                                else if (st == "Retired")
                                    Archers[i].state = State.Retired;
                                else
                                    Archers[i].state = State.Inuse;
                                Archers[i].entryref = getparam(str, "entryref");
                                Archers[i].forename = getparam(str, "forename");
                                Archers[i].surname = getparam(str, "surname");
                                Archers[i].club = getparam(str, "club");
                                Archers[i].team = getparam(str, "team");
                                Archers[i].bowtype = getparam(str, "bow");
                                Archers[i].gender = getparam(str, "gender");
                                Archers[i].dateofbirth = getparam(str, "dateofbirth");
                                Archers[i].agegroup = AgeGroup(tournamentdate, Archers[i].dateofbirth, Archers[i].gender);
                                Archers[i].classification = getparam(str, "class");
                                Archers[i].agbno = getparam(str, "membership");
                                Archers[i].notes = getparam(str, "notes");
                                try
                                {
                                    Archers[i].handicap = Convert.ToSingle(getparam(str, "handicap"));
                                }
                                catch
                                {
                                    Archers[i].handicap = 100;
                                }
                                Archers[i].round = getparam(str, "round");
                                // make all space on that target the same round
                                for (int j = -5; j < 6; j++)
                                {
                                    if (j != 0 && i + j >= 0 && i + j < MAXARCHERS
                                        && Archers[i + j].target.Substring(0, 2) == Archers[i].target.Substring(0, 2))
                                    {
                                        if (Archers[i + j].round == "NotSet")
                                            Archers[i + j].round = Archers[i].round;
                                    }
                                }
                            }
                        }
                        if (u == UPDATE.All || u == UPDATE.Score)
                        {
                            if (st == "DNS")
                                Archers[i].state = State.DNS;
                            else if (st == "DQ")
                                Archers[i].state = State.DQ;
                            else if (st == "Retired")
                                Archers[i].state = State.Retired;
                            else
                                Archers[i].state = State.Inuse;
                            try
                            {
                                Archers[i].runningtotal = Convert.ToInt32(getparam(str, "tscore"));
                                Archers[i].tiebreak1 = Convert.ToInt32(getparam(str, "thits"));
                                Archers[i].tiebreak2 = Convert.ToInt32(getparam(str, "tgolds"));
                                if (getparam(str, "arrowsend") != "NotFound")
                                {
                                    Archers[i].arrows = getparam(str, "arrowsend");
                                    Archers[i].arrowcnt = Archers[i].arrows.Length;
                                }
                            }
                            catch
                            {
                                Console.WriteLine("Error in score {0} {1} {2} {3}", getparam(str, "tscore"),
                                    getparam(str, "thits"), getparam(str, "tgolds"), getparam(str, "arrowsend"));
                            };
                        }
                        timeout = TIMEOUT;
                        break;
                    }
                }
            }
        }

        static string ArcherData(int i, string dataname, bool input, bool paid)
        {
            return dataname + " = new Array(\"" + Archers[i].target + "\",\""
                + Archers[i].surname + "\",\"" + Archers[i].forename + "\",\""
                + Archers[i].club + "\",\"" + Archers[i].team + "\",\"" + Archers[i].bowtype + "\",\""
                + Archers[i].gender + "\",\""
                + (input ? Archers[i].dateofbirth : Archers[i].agegroup) + "\",\""
                + Archers[i].round + "\"," + Archers[i].handicap.ToString("0.0") + ","
                + Archers[i].runningtotal + "," + Archers[i].tiebreak1 + ","
                + Archers[i].tiebreak2 + ",\"" + Archers[i].state + "\"," + Archers[i].arrowcnt
                + ",\"" + Archers[i].arrows + "\",\"" + Archers[i].classification + "\",\"" + Archers[i].agbno
                + "\",\"" + Archers[i].notes + "\",\"" + Archers[i].entryref + "\""
                + (paid ? ",\"" + Archers[i].paid + "\"" : "")
                + ");\r\n";
        }

        static string ArcherDataStr(string target, bool skip, string dataname, bool input)
        {
            string results = "";
            lock (dataLock)
            {
                for (int i = 0; i < MAXARCHERS; i++)
                {
                    if (Archers[i].target == target)
                    {
                        if (skip)
                        {
                            i++;
                            if (i == MAXARCHERS)
                                i = 0;
                        }
                        results = ArcherData(i, dataname, input, false);
                        break;
                    }
                }
            }
            return results;
        }

        static string SendScorePage(string scoreentry, string target, bool skip, bool back)
        {
            // Console.WriteLine("SendScorePage {0} {1}", target, skip);
            string results = header + "<div id=\"page\"></div><script type=\"text/javascript\">\r\n";
            bool targetfound = false;
            bool ok = false;
            int i = 0;
            lock (dataLock)
            {
                if (back)
                {
                    for (i = MAXARCHERS - 1; i >= 0; i--)
                    {
                        if (Archers[i].target == target)
                            targetfound = true;
                        if (targetfound && skip)
                            skip = false;
                        else if (targetfound && Archers[i].state != State.Free)
                        {
                            ok = true;
                            break;
                        }
                    }
                }
                else
                {
                    for (i = 0; i < MAXARCHERS; i++)
                    {
                        if (Archers[i].target == target)
                            targetfound = true;
                        if (targetfound && skip)
                            skip = false;
                        else if (targetfound && Archers[i].state == State.Inuse)
                        {
                            ok = true;
                            break;
                        }
                    }
                }
                if (ok)
                {
                    results += ArcherData(i, "var data", false, false) + fieldnames + flagstring();
                }
            }
            if (ok)
            {
                // Console.WriteLine("Sending score page for target {0}.", Archers[i].target);
                return results + scoreentry;
            }
            else
                return indexpagestring();
        }

        public static string GetLocalIP()
        {
            string ipv4Address = String.Empty;
            foreach (IPAddress currrentIPAddress in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (currrentIPAddress.AddressFamily.ToString() == System.Net.Sockets.AddressFamily.InterNetwork.ToString())
                {
                    localipaddress = currrentIPAddress;
                    ipv4Address = currrentIPAddress.ToString();
                    break;
                }
            }
            return ipv4Address;
        }

        private static void UpdateSend(Socket handler, byte[] byteData)
        {
            try
            {
                // Convert the string data to byte data using ASCII encoding.
                //byte[] byteData = Encoding.UTF8.GetBytes(data);

                // Begin sending the data to the remote device.
                handler.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(UpdateSendCallback), handler);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void UpdateSendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);
                Console.WriteLine("Sent update {0} bytes.", bytesSent);

                handler.Shutdown(SocketShutdown.Both);
                handler.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        //**********************************************************************************************************************
        //
        //  File backup thread
        //
        //**********************************************************************************************************************

        static int timeout = -1;

        public static void Backup()
        {
            while (!shutdown)
            {
                Thread.Sleep(1000);
                lock (dataLock)
                {
                    if (timeout >= 0)
                    {
                        if (timeout == 0)
                        {
                            Console.WriteLine("Backup.");
                            SaveDataFile(FilePath + "data.csv");
                            AGBDataFile(FilePath + "agbexport.csv");
                        }
                        timeout--;
                    }
                }
            }
        }

        static void SaveDataFile(string filename)
        {
            string[] cols = new string[] { "ENTRYREF", "TARGET", "FORENAME", "SURNAME", "CLUB", "TEAM", "BOW", "GENDER",
                "DATEOFBIRTH", "ROUND", "HANDICAP", "TOTAL", "TIEBREAK1", "TIEBREAK2", "STATE", "ARROWCNT",
                "ARROWS", "CLASS", "AGB", "NOTES", "PAID", "FEE", "EMAIL", "ADDRESS", "PHONE" };
            string colstr = "", maxstr;
            int i;
            maxstr = maxtargets.ToString();
            if (maxstr.Length == 1)
                maxstr = "0" + maxstr;
            for (i = 0; i < cols.Length; i++)
                colstr += cols[i] + ",";
            StreamWriter file = File.CreateText(filename);
            lock (dataLock)
            {
                // Delete free spaces at end
                int max;
                for (max = MAXARCHERS; max > 1; max--)
                {
                    if (Archers[max - 1].target.Substring(0, 2) == maxstr)
                        break;
                    if (Archers[max - 1].state != State.Free)
                        break;
                }
                // save
                file.WriteLine("VER:,2,");
                file.WriteLine("HEADER:,\"" + tournament + "\"");
                file.WriteLine("VENUE:,\"" + venue + "\"");
                file.WriteLine("TO:,\"" + tournamentorganiser + "\"");
                file.WriteLine("DATE:,\"" + tournamentdate + "\"");
                file.WriteLine("TIME:,\"" + timeofassembly + "\"");
                file.WriteLine("JUDGES:,\"" + judges + "\"");
                file.WriteLine("PARAMOUNT:,\"" + paramount + "\"");
                file.WriteLine("PATRON:,\"" + patron + "\"");
                file.WriteLine("WEATHER:,\"" + weather + "\"");
                file.WriteLine("TARGETS:," + maxtargets);
                file.WriteLine("OPTIONS:," + (medalflag == 1 ? "Medals " : "")
                    + (juniorflag == 1 ? "Junior " : "") + (bestflag == 1 ? "Best " : "")
                    + (handicapflag == 1 ? "Handicap " : "")
                    + (teamflag == 1 ? "Team " : "") + (resultsbyroundflag == 1 ? "Round " : "")
                    // + (worldarchery == 1 ? "WorldArchery " : (worldarchery == 2 ? "WorldArcheryIndoor " : ""))
                    + (scoresystem == 1 ? "Dozen " : (scoresystem == 2 ? "Total " : "")));
                file.WriteLine("ARCHERS:,");
                file.WriteLine(colstr);
                for (i = 0; i < max; i++)
                {
                    string s = "\"" + Archers[i].entryref + "\"," + Archers[i].target + ","
                        + Archers[i].forename + "," + Archers[i].surname
                        + "," + Archers[i].club + ","
                        + Archers[i].team + "," + Archers[i].bowtype + ","
                        + Archers[i].gender + "," + Archers[i].dateofbirth + ","
                        + Archers[i].round + "," + Archers[i].handicap.ToString("0.0") + ","
                        + Archers[i].runningtotal + "," + Archers[i].tiebreak1 + "," + Archers[i].tiebreak2 + ","
                        + Archers[i].state + "," + Archers[i].arrowcnt + "," + Archers[i].arrows + ","
                        + "\"" + Archers[i].classification + "\",\"" + Archers[i].agbno + "\",\"" + Archers[i].notes + "\","
                        + "\"" + Archers[i].paid + "\"," + Archers[i].fee + "," + Archers[i].email + ","
                        + Archers[i].address + ",\"" + Archers[i].phone + "\",";
                    file.WriteLine(s);
                }
                file.Close();
            }
        }

        static void AGBDataFile(string filename)
        {
            StreamWriter file = File.CreateText(filename);
            lock (dataLock)
            {
                string round = Archers[0].round;
                if (round.Length == 0)
                    round = Archers[1].round;
                string[] tie = tiebreak(Archers[1].round);
                file.WriteLine("\"" + tournament + "\",\"" + tournamentdate + "\",\"" + venue + "\",");
                file.WriteLine("\"AGB NUMBER\",FORENAME,SURNAME,GENDER,BOWSTYLE,\"AGE GROUP\",SCORE,\""
                    + tie[0] + "\",\"" + tie[1] + "\",ROUND,");
                for (int i = 0; i < MAXARCHERS; i++)
                {
                    if (Archers[i].state == State.Inuse)
                    {
                        string s = Archers[i].agbno + "," + Archers[i].forename + "," + Archers[i].surname + ","
                            + Archers[i].gender[0] + "," + Archers[i].bowtype + "," + Archers[i].agegroup + ","
                            + Archers[i].runningtotal + "," + Archers[i].tiebreak1 + "," + Archers[i].tiebreak2 + ","
                            + Archers[i].round + ",";
                        file.WriteLine(s);
                    }
                }
                file.Close();
            }
        }

        static string AgeGroup(string tourdate, string archerdate, string gender)
        {
            if (worldarchery == 0)
            {
                if (archerdate == "U10" || archerdate == "U12" || archerdate == "U14" || archerdate == "U16" || archerdate == "U18")
                    return archerdate;
                if (archerdate == "CW" || archerdate == "CM")
                    return "U18";
                if (archerdate == "JM" || archerdate == "JW" || archerdate == "MW" || archerdate == "MM")
                    return "";
                if (tourdate.Length != 10)
                    return "";
                if (archerdate.Length != 10)
                    return "";
                string ammdd = archerdate.Substring(3, 2) + archerdate.Substring(0, 2);
                string tmmdd = tourdate.Substring(3, 2) + tourdate.Substring(0, 2);
                int age = Convert.ToInt16(tourdate.Substring(6, 4)) - Convert.ToInt16(archerdate.Substring(6, 4));
                if (string.Compare(tmmdd, ammdd) < 0)
                    age = age - 1;
                if (age >= 18)
                    return "";
                if (age >= 16)
                    return "U18";
                if (age >= 14)
                    return "U16";
                if (age >= 12)
                    return "U14";
                if (age >= 10)
                    return "U12";
                return "U10";
            }
            else // world archery age groups based on 1st Jan
            {
                if (archerdate == "CW" || archerdate == "CM" || archerdate == "JM" || archerdate == "JW"
                    || archerdate == "MW" || archerdate == "MM")
                    return archerdate;
                if (archerdate == "U10" || archerdate == "U12" || archerdate == "U14" || archerdate == "U16" || archerdate == "U18")
                    return "C" + gender;
                if (tourdate.Length != 10)
                    return "";
                if (archerdate.Length != 10)
                    return "";
                int age = Convert.ToInt16(tourdate.Substring(6, 4)) - Convert.ToInt16(archerdate.Substring(6, 4));
                if (age <= 17) // up to 17th birthday and the rest of year
                    return "C" + gender;
                if (age >= 50) // for the year of 50th birthday
                    return "M" + gender;
                if (age <= 20) // for the year of 18th, up to 20th birthday and rest of year
                    return "J" + gender;
                return gender; // 21 to 49
            }
        }

        static string[] tiebreak(string round)
        {
            string[] result = { "HITs", "GOLDs" };
            int startp = rounds.IndexOf(round, 0);
            if (round.Length == 0 || startp < 0)
                return result;
            int eol = rounds.IndexOf("\n", startp);
            string line = rounds.Substring(startp, eol - startp);
            if (line.Contains("TieBreakAGBImperial"))
                return result;
            if (line.Contains("TieBreakAGBMetric"))
                return new string[] { "HITs", "10s" };
            if (line.Contains("TieBreakAGBWorcester"))
                return new string[] { "HITs", "5s" };
            if (line.Contains("TieBreakWAOutdoor"))
                return new string[] { "X+10s", "Xs" };
            if (line.Contains("TieBreakWAIndoor"))
                return new string[] { "10s", "9s" };
            return result;
        }

        //**********************************************************************************************************************
        //
        //  Main thread
        //
        //**********************************************************************************************************************

        static string ReadFile(String filename)
        {
            // Open the stream and read it back. 
            FileStream fs = File.Open(filename, FileMode.Open);
            int filesize = (int)fs.Length;
            byte[] b = new byte[filesize];
            UTF8Encoding temp = new UTF8Encoding(true);
            fs.Read(b, 0, filesize);
            fs.Close();
            return temp.GetString(b);
        }

        static byte[] ReadLogo(String filename)
        {
            // Open the stream and read it back. 
            FileStream fs = File.Open(filename, FileMode.Open);
            int filesize = (int)fs.Length;
            byte[] s = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/gif;\r\nContent-Length: "
                + filesize + "\r\nConnection: close\r\n\r\n");
            byte[] b = new byte[s.Length + filesize];
            UTF8Encoding temp = new UTF8Encoding(true);
            fs.Read(b, s.Length, filesize);
            fs.Close();
            System.Buffer.BlockCopy(s, 0, b, 0, s.Length);
            return b;
        }

        static void processcol(string tag, string str)
        {
            int i;
            int no = 0;
            float fno = 0;
            if (str.Length == 0)
                return;
            switch (tag)
            {
                case "ENTRYREF":
                    Archers[MaxArchers].entryref = str;
                    break;
                case "TARGET":
                    if (str.Length == 2)
                        str = "0" + str;
                    Archers[MaxArchers].target = str;
                    break;
                case "NAME":
                    Archers[MaxArchers].forename = str;
                    i = str.IndexOf(' ');
                    if (i >= 0)
                    {
                        Archers[MaxArchers].forename = str.Substring(0, i);
                        Archers[MaxArchers].surname = str.Substring(i + 1);
                        Archers[MaxArchers].state = State.Inuse;
                    }
                    break;
                case "FORENAME":
                    Archers[MaxArchers].forename = str;
                    if (str.Length != 0)
                        Archers[MaxArchers].state = State.Inuse;
                    break;
                case "SURNAME":
                    Archers[MaxArchers].surname = str;
                    if (str.Length != 0)
                        Archers[MaxArchers].state = State.Inuse;
                    break;
                case "CLUB":
                    Archers[MaxArchers].club = str;
                    break;
                case "TEAM":
                    Archers[MaxArchers].team = str;
                    break;
                case "BOW":
                    if (str.Length >= 1)
                        str = str.Substring(0, 1).ToUpper();
                    if (str.Length > 0 && !"CRBL".Contains(str))
                        Console.WriteLine("Not a valid bow type {0} {1}", str,
                            Archers[MaxArchers].forename + " " + Archers[MaxArchers].surname);
                    else if (str.Length == 0 && (Archers[MaxArchers].forename + " " + Archers[MaxArchers].surname).Length != 1)
                        Console.WriteLine("No bow for {0}", Archers[MaxArchers].forename + " " + Archers[MaxArchers].surname);
                    Archers[MaxArchers].bowtype = "Recurve";
                    switch (str)
                    {
                        case "C": Archers[MaxArchers].bowtype = "Compound"; break;
                        case "B": Archers[MaxArchers].bowtype = "Bare Bow"; break;
                        case "L": Archers[MaxArchers].bowtype = "Longbow"; break;
                    }
                    break;
                case "GENDER":
                    str = str.ToUpper();
                    if (str.Length >= 1)
                    {
                        if (str[0] == 'F')
                            str = "W" + str.Substring(1);
                    }
                    if (str.Length >= 2)
                    {
                        if (str[1] == 'B')
                            str = "JM" + str.Substring(2);
                        else if (str[1] == 'G')
                            str = "JW" + str.Substring(2);
                        if (str[0] == 'J')
                        {
                            Archers[MaxArchers].gender = str.Substring(1, 1);
                            Archers[MaxArchers].agegroup = str.Substring(2);
                        }
                    }
                    if (str[0] == 'M' || str[0] == 'W')
                    {
                        Archers[MaxArchers].gender = str.Substring(0, 1);
                        if (str.Length > 1)
                        {
                            if (str.Length == 9)
                            {
                                if (str[7] == '7' || str[7] == '8' || str[7] == '9')
                                    str = str.Substring(0, 7) + "19" + str.Substring(7, 2);
                                else
                                    str = str.Substring(0, 7) + "20" + str.Substring(7, 2);
                            }
                            Archers[MaxArchers].dateofbirth = str.Substring(1);
                            Archers[MaxArchers].agegroup = AgeGroup(tournamentdate, str.Substring(1), Archers[MaxArchers].gender);
                        }
                    }
                    break;
                case "DATEOFBIRTH":
                    Archers[MaxArchers].dateofbirth = str;
                    Archers[MaxArchers].agegroup = AgeGroup(tournamentdate, str, Archers[MaxArchers].gender);
                    break;
                case "ROUND":
                    Archers[MaxArchers].round = str;
                    if (str == "")
                        Archers[MaxArchers].state = State.Free;
                    else
                        Archers[MaxArchers].state = State.Inuse;
                    break;
                case "HANDICAP":
                    fno = 100;
                    try
                    {
                        fno = Convert.ToSingle(str);
                    }
                    catch
                    {
                        Console.WriteLine("Invalid HANDICAP in {0} {1}.", str,
                            Archers[MaxArchers].forename + " " + Archers[MaxArchers].surname);
                    }
                    Archers[MaxArchers].handicap = fno;
                    break;
                case "TOTAL":
                    no = 0;
                    try
                    {
                        no = Convert.ToInt32(str);
                    }
                    catch
                    {
                        Console.WriteLine("Invalid TOTAL in {0} {1}.", str,
                            Archers[MaxArchers].forename + " " + Archers[MaxArchers].surname);
                    }
                    Archers[MaxArchers].runningtotal = no;
                    break;
                case "TIEBREAK1":
                    no = 0;
                    try
                    {
                        no = Convert.ToInt32(str);
                    }
                    catch
                    {
                        Console.WriteLine("Invalid TIEBREAK1 in {0} {1}.", str,
                            Archers[MaxArchers].forename + " " + Archers[MaxArchers].surname);
                    }
                    Archers[MaxArchers].tiebreak1 = no;
                    break;
                case "TIEBREAK2":
                    no = 0;
                    try
                    {
                        no = Convert.ToInt32(str);
                    }
                    catch
                    {
                        Console.WriteLine("Invalid TIEBREAK2 in {0} {1}.", str,
                            Archers[MaxArchers].forename + " " + Archers[MaxArchers].surname);
                    }
                    Archers[MaxArchers].tiebreak2 = no;
                    break;
                case "STATE":
                    Archers[MaxArchers].state = State.Inuse;
                    if (str == "DNS")
                        Archers[MaxArchers].state = State.DNS;
                    else if (str == "DQ")
                        Archers[MaxArchers].state = State.DQ;
                    else if (str == "Retired")
                        Archers[MaxArchers].state = State.Retired;
                    else if (str == "Free")
                        Archers[MaxArchers].state = State.Free;
                    break;
                case "ARROWCNT":
                    no = 0;
                    try
                    {
                        no = Convert.ToInt32(str);
                    }
                    catch
                    {
                        Console.WriteLine("Invalid ARROWCNT in {0} {1}.", str,
                            Archers[MaxArchers].forename + " " + Archers[MaxArchers].surname);
                    }
                    Archers[MaxArchers].arrowcnt = no;
                    break;
                case "ARROWS":
                    Archers[MaxArchers].arrows = str;
                    break;
                case "CLASS":
                    if ("1st;2nd;3rd;JB;JMB;GMB;MB;B".Contains(str))
                        Archers[MaxArchers].classification = str;
                    else
                        Archers[MaxArchers].classification = " ";
                    break;
                case "AGB":
                    Archers[MaxArchers].agbno = str;
                    break;
                case "NOTES":
                    Archers[MaxArchers].notes = str;
                    break;
                case "PAID":
                    Archers[MaxArchers].paid = str;
                    break;
                case "FEE":
                    float v = 0;
                    try
                    {
                        v = Convert.ToSingle(str);
                    }
                    catch
                    {
                        Console.WriteLine("Invalid FEE in {0} {1}.", str,
                            Archers[MaxArchers].forename + " " + Archers[MaxArchers].surname);
                    }
                    Archers[MaxArchers].fee = v;
                    break;
                case "EMAIL":
                    Archers[MaxArchers].email = str;
                    break;
                case "ADDRESS":
                    Archers[MaxArchers].address = str;
                    break;
                case "PHONE":
                    Archers[MaxArchers].phone = str;
                    break;
            }
        }

        static string getstr(string str, ref int startp, int endline)
        {
            string s = "";
            if (str[startp] == '\"')
            {
                int endp = str.IndexOf('\"', startp + 1);
                if (endp > 0 && endp < endline)
                {
                    s = str.Substring(startp + 1, endp - startp - 1).Trim();
                    startp = endp + 2;
                }
            }
            else
            {
                int endp = str.IndexOf(',', startp);
                if (endp > endline)
                    endp = endline;
                if (endp > 0 && startp < endline)
                    s = str.Substring(startp, endp - startp).Trim();
                startp = endp + 1;
            }
            return s;
        }

        // read main database file as CSV, from Excel or OpenOffice Calc
        static void ReadDataFile(string filename)
        {
            FileStream fs = File.Open(filename, FileMode.Open);
            int filesize = (int)fs.Length;
            byte[] b = new byte[filesize];
            string[] incols = new string[100];
            UTF8Encoding temp = new UTF8Encoding(true);
            fs.Read(b, 0, filesize);
            fs.Close();
            string s = temp.GetString(b);
            int startp = 0;
            int coma = 0;
            // Tournament data
            int endline = s.IndexOf('\n');
            string str = getstr(s, ref startp, endline).ToUpper();
            if (str == "VER:") // version 2 on
            {
                // each line has a title and the data
                tournament = "TBA";
                venue = "TBA";
                judges = "TBA";
                paramount = "TBA";
                patron = "TBA";
                tournamentorganiser = "TBA";
                timeofassembly = "TBA";
                weather = "TBA";
                tournamentdate = "01/01/2000";
                medalflag = 0;
                juniorflag = 0;
                bestflag = 0;
                teamflag = 0;
                handicapflag = 0;
                resultsbyroundflag = 0;
                scoresystem = 0;
                bool startarchers = false;
                while (!startarchers && startp < s.Length)
                {
                    endline = s.IndexOf('\n', startp);
                    if (endline < 0)
                        break;
                    coma = s.IndexOf(',', startp);
                    if (coma > endline)
                        coma = endline;
                    str = getstr(s, ref startp, endline);
                    startp = coma + 1;
                    switch (str.ToUpper())
                    {
                        case "ARCHERS:":
                            startarchers = true;
                            break;
                        case "DATE:":
                            tournamentdate = getstr(s, ref startp, endline);
                            if (tournamentdate.Length == 8)
                                tournamentdate = tournamentdate.Substring(0, 6) + "20" + tournamentdate.Substring(6, 2);
                            break;
                        case "HEADER:":
                            tournament = getstr(s, ref startp, endline);
                            break;
                        case "JUDGES:":
                            judges = getstr(s, ref startp, endline);
                            break;
                        case "OPTIONS:":
                            coma = s.IndexOf(',', startp);
                            if (coma > endline)
                                coma = endline;
                            medalflag = s.Substring(startp, coma - startp).Contains("Medals") ? 1 : 0;
                            juniorflag = s.Substring(startp, coma - startp).Contains("Junior") ? 1 : 0;
                            bestflag = s.Substring(startp, coma - startp).Contains("Best") ? 1 : 0;
                            teamflag = s.Substring(startp, coma - startp).Contains("Team") ? 1 : 0;
                            handicapflag = s.Substring(startp, coma - startp).Contains("Handicap") ? 1 : 0;
                            resultsbyroundflag = s.Substring(startp, coma - startp).Contains("Round") ? 1 : 0;
                            // worldarchery = s.Substring(startp, coma - startp).Contains("WorldArcheryIndoor") ? 2 
                            //    : s.Substring(startp, coma - startp).Contains("WorldArchery") ? 1 : 0;
                            scoresystem = 0;
                            if (s.Substring(startp, coma - startp).Contains("Dozen"))
                                scoresystem = 1;
                            else if (s.Substring(startp, coma - startp).Contains("Total"))
                                scoresystem = 2;
                            startp = coma + 1;
                            break;
                        case "PARAMOUNT:":
                            paramount = getstr(s, ref startp, endline);
                            break;
                        case "PATRON:":
                            patron = getstr(s, ref startp, endline);
                            break;
                        case "TARGETS:":
                            str = getstr(s, ref startp, endline);
                            try
                            {
                                maxtargets = Convert.ToInt32(str);
                                if (maxtargets < 2)
                                    maxtargets = 2;
                                if (maxtargets > 99)
                                    maxtargets = 99;
                            }
                            catch
                            {
                                Console.WriteLine("Max targets is not a number");
                            }
                            break;
                        case "TIME:":
                            timeofassembly = getstr(s, ref startp, endline);
                            break;
                        case "TO:":
                            tournamentorganiser = getstr(s, ref startp, endline);
                            break;
                        case "VENUE:":
                            venue = getstr(s, ref startp, endline);
                            break;
                        case "VER:":
                            break;
                        case "WEATHER:":
                            weather = getstr(s, ref startp, endline);
                            break;
                    }
                    startp = endline + 1;
                }
            }
            else
            { // version 1
                // First line Tournament name, maximum targets, Options, date etc...
                tournament = str;
                // maxtargets
                string t = getstr(s, ref startp, endline);
                try
                {
                    maxtargets = Convert.ToInt32(t);
                    if (maxtargets < 2)
                        maxtargets = 2;
                    if (maxtargets > 99)
                        maxtargets = 99;
                }
                catch
                {
                    Console.WriteLine("Max targets is not a number");
                }
                // options
                coma = s.IndexOf(',', startp);
                if (coma > endline)
                    coma = endline;
                medalflag = s.Substring(startp, coma - startp).Contains("Medals") ? 1 : 0;
                juniorflag = s.Substring(startp, coma - startp).Contains("Junior") ? 1 : 0;
                bestflag = s.Substring(startp, coma - startp).Contains("Best") ? 1 : 0;
                teamflag = s.Substring(startp, coma - startp).Contains("Team") ? 1 : 0;
                handicapflag = s.Substring(startp, coma - startp).Contains("Handicap") ? 1 : 0;
                resultsbyroundflag = s.Substring(startp, coma - startp).Contains("Round") ? 1 : 0;
                // worldarchery = s.Substring(startp, coma - startp).Contains("WorldArcheryIndoor") ? 2 
                //    : s.Substring(startp, coma - startp).Contains("WorldArchery") ? 1 : 0;
                scoresystem = 0;
                if (s.Substring(startp, coma - startp).Contains("Dozen"))
                    scoresystem = 1;
                else if (s.Substring(startp, coma - startp).Contains("Total"))
                    scoresystem = 2;
                startp = coma + 1;

                // date etc
                tournamentdate = getstr(s, ref startp, endline);
                if (tournamentdate.Length == 8)
                    tournamentdate = tournamentdate.Substring(0, 6) + "20" + tournamentdate.Substring(6, 2);
                venue = getstr(s, ref startp, endline);
                judges = getstr(s, ref startp, endline);
                paramount = getstr(s, ref startp, endline);
                patron = getstr(s, ref startp, endline);
                tournamentorganiser = getstr(s, ref startp, endline);
                timeofassembly = getstr(s, ref startp, endline);
                weather = getstr(s, ref startp, endline);
            }
            startp = endline + 1;

            // Second line col headings
            endline = s.IndexOf('\n', startp);
            // Console.WriteLine(s.Substring(startp, endline - startp));
            int maxcol = 0;
            while (startp < endline)
            {
                str = getstr(s, ref startp, endline).ToUpper();
                if (str.Length <= 1)
                    break;
                incols[maxcol] = str;
                incols[++maxcol] = "";
            }
            startp = endline + 1;

            // Body of file with archer data
            MaxArchers = 0;
            lock (dataLock)
            {
                while (startp < s.Length)
                {
                    Archers[MaxArchers] = new Archer();
                    endline = s.IndexOf('\n', startp);
                    if (endline < 0)
                        break;
                    // Console.WriteLine(s.Substring(startp, endline - startp));
                    for (int col = 0; col < maxcol; col++)
                    {
                        str = "";
                        if (startp < endline)
                        {
                            str = getstr(s, ref startp, endline);
                            processcol(incols[col], str);
                        }
                    }
                    if (Archers[MaxArchers].state == State.Free)
                        Archers[MaxArchers].Clear();
                    startp = endline + 1;
                    MaxArchers++;
                }
            }
        }

        public static void sortbytarget()
        {
            lock (dataLock)
            {
                bool swap = true;
                while (swap)
                {
                    swap = false;
                    for (int j = 0; j < MaxArchers - 1; j++)
                    {
                        if (Archers[j + 1].target != null && string.Compare(Archers[j].target, Archers[j + 1].target) > 0)
                        {
                            Archer t = Archers[j];
                            Archers[j] = Archers[j + 1];
                            Archers[j + 1] = t;
                            swap = true;
                        }
                    }
                }
            }
        }

        public static void insertfreetargets()
        {
            char[] letter = new char[6] { 'A', 'B', 'C', 'D', 'E', 'F' };
            int t = ARCHERSPERTARGET;
            string s = (t / ARCHERSPERTARGET).ToString("0#") + letter[t % ARCHERSPERTARGET];
            lock (dataLock)
            {
                // Delete free space at end
                int max;
                for (max = MaxArchers; max > 1; max--)
                {
                    if (Archers[max - 1] != null && Archers[max - 1].state != State.Free && Archers[max - 1].round != "")
                        break;
                }
                string roundtype = "NotSet";
                MaxArchers = max;
                // add missing spaces
                for (int i = 0; i < max; i++)
                {
                    s = (t / ARCHERSPERTARGET).ToString("0#") + letter[t % ARCHERSPERTARGET];
                    if (t % ARCHERSPERTARGET == 0)
                    {
                        for (int j = i; j < i + ARCHERSPERTARGET; j++)
                        {
                            if (Archers[j].round != "")
                            {
                                roundtype = Archers[j].round;
                                break;
                            }
                        }
                    }
                    while (string.Compare(Archers[i].target, s) > 0)
                    {
                        if (t % ARCHERSPERTARGET == 0)
                        {
                            for (int j = i; j < i + ARCHERSPERTARGET; j++)
                            {
                                if (Archers[j].round != "")
                                {
                                    roundtype = Archers[j].round;
                                    break;
                                }
                            }
                        }
                        Archers[MaxArchers] = new Archer();
                        Archers[MaxArchers].target = s;
                        Archers[MaxArchers].round = roundtype;
                        MaxArchers++;
                        if (MaxArchers == MAXARCHERS)
                            break;
                        t++;
                        s = (t / ARCHERSPERTARGET).ToString("0#") + letter[t % ARCHERSPERTARGET];
                    }
                    if (string.Compare(Archers[i].target, s) == 0)
                    {
                        t++;
                        roundtype = Archers[i].round;
                    }
                }
                // add free space at the end
                while (MaxArchers < MAXARCHERS)
                {
                    s = (t / ARCHERSPERTARGET).ToString("0#") + letter[t % ARCHERSPERTARGET];
                    if (t % ARCHERSPERTARGET == 0)
                        roundtype = "NotSet";
                    Archers[MaxArchers] = new Archer();
                    Archers[MaxArchers].target = s;
                    Archers[MaxArchers].round = roundtype;
                    MaxArchers++;
                    t++;
                }
            }
        }

        public static void deletefree()
        {
            int j = 1;
            int i = 0;
            do
            {
                if (Archers[i].state == State.Free)
                {
                    while (j < MAXARCHERS && Archers[j].state == State.Free)
                        j++;
                    if (j >= MAXARCHERS)
                        break;
                    Archer t = Archers[i];
                    Archers[i] = Archers[j];
                    Archers[j] = t;
                }
                i++;
                j = i + 1;
            } while (i < MAXARCHERS);
        }

        public static void checktargetnumbers()
        {
            char[] letter = new char[6] { 'A', 'B', 'C', 'D', 'E', 'F' };
            int t = ARCHERSPERTARGET;
            for (int i = 0; i < MAXARCHERS; i++, t++)
            {
                string s = (t / ARCHERSPERTARGET).ToString("0#") + letter[t % ARCHERSPERTARGET];
                if (Archers[i].target == "")
                    Console.WriteLine("No target number {0}", Archers[i].forename + " " + Archers[i].surname);
                else if (i > 0 && Archers[i].target == Archers[i - 1].target)
                    Console.WriteLine("Duplicate target numbers {0}", Archers[i].target);
                else if (String.Compare(Archers[i].target, s) < 0)
                {
                    Console.WriteLine("Extra Target {0}", Archers[i].target);
                    t--;
                }
                else if (String.Compare(Archers[i].target, s) > 0)
                {
                    Console.WriteLine("Missing Target {0}", s);
                    t++;
                }
            }
        }

        public static void Readcfg()
        {
            UTF8Encoding temp = new UTF8Encoding(true);
            byte[] b;
            string s = "";
            try
            {
                FileStream fs = File.Open("CPBserver.cfg", FileMode.Open);
                int filesize = (int)fs.Length;
                b = new byte[filesize];
                fs.Read(b, 0, filesize);
                fs.Close();
            }
            catch
            {
                Console.WriteLine("Configuration missing or not opened.");
                return;
            };
            try
            {
                s = temp.GetString(b);
                FilePath = getparam(s, "Path");
                RESULTPORT = Convert.ToInt32(getparam(s, "Result"));
                UPDATEPORT = Convert.ToInt32(getparam(s, "Update"));
                TIMEOUT = Convert.ToInt32(getparam(s, "Timeout"));
            }
            catch
            {
                Console.WriteLine("Error in configuration data {0}", s);
            };
        }

        public static void readtxts()
        {
            results = ReadFile(FilePath + "results.txt");
            updateentry = ReadFile(FilePath + "updateentry.txt");
            movearcher = ReadFile(FilePath + "movearcher.txt");
            byname = ReadFile(FilePath + "byname.txt");
            indexpage = ReadFile(FilePath + "index.txt");
            scoreentry = ReadFile(FilePath + "scoreentry.txt");
            scoresheet = ReadFile(FilePath + "scoresheet.txt");
            allocate = ReadFile(FilePath + "allocate.txt");
            newarcher = ReadFile(FilePath + "newarcher.txt");
            setuppage = ReadFile(FilePath + "setup.txt");
            printrun = ReadFile(FilePath + "printrun.txt");
            printscore = ReadFile(FilePath + "printscore.txt");
            rounds = ReadFile(FilePath + "rounds.txt");
            printpage = ReadFile(FilePath + "print.txt");
            hc2dozen = ReadFile(FilePath + "hc2dozen.txt");
            logo = ReadLogo(FilePath + "logo.gif");
        }

        public static int Main(String[] args)
        {
            Console.Write("CPBserver  Copyright (C) 2014 Mike Johnson\r\n"
                + "This program comes with ABSOLUTELY NO WARRANTY.\r\n"
                + "This is free software, and you are welcome to redistribute it.\r\n"
                + "See gpl.txt or http://www.gnu.org/licenses/ for conditions.\r\n\n");
            Readcfg();
            Console.WriteLine("Server address {0} ports {1}, {2}\r\nDirectory {3}, TIMEOUT {4}",
                GetLocalIP(), RESULTPORT, UPDATEPORT, FilePath, TIMEOUT);
            ReadDataFile(FilePath + "data.csv");
            sortbytarget();
            insertfreetargets();
            sortbytarget();
            checktargetnumbers();
            // SaveDataFile(FilePath + "data.csv");
            readtxts();

            Thread resultsThread = new Thread(new ThreadStart(Results));
            resultsThread.Start();
            Thread updateThread = new Thread(new ThreadStart(Updates));
            updateThread.Start();
            Thread backupThread = new Thread(new ThreadStart(Backup));
            backupThread.Start();

            while (!shutdown)
            {
                string s = Console.ReadLine();
                if (s.Contains("shutdown"))
                    shutdown = true;
                if (s.Contains("close"))
                    resultsclosed = true;
                if (s.Contains("open"))
                    resultsclosed = false;
                if (s.Contains("read"))
                    readtxts();
            }

            backupThread.Join();
            resultsDone.Set();
            updateDone.Set();
            resultsThread.Join();
            updateThread.Join();
            SaveDataFile(FilePath + "data.csv");
            AGBDataFile(FilePath + "agbexport.csv");
            return 0;
        }
    }
}
