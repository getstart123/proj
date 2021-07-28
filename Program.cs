using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using JiebaNet.Segmenter;
using System.IO;

namespace Analysis
{
    static class Program
    {
        static int page = 1;
        public enum months { 
            Jan = 1, JAN = 1, Feb, FEB = 2, Mar, MAR = 3, Apr, APR = 4, May, MAY = 5, Jun, JUN = 6,
            Jul, JUL = 7, Aug, AUG = 8, Sep, SEP = 9, Oct, OCT = 10, Nov, NOV = 11, Dec, DEC = 12,
            Spr = 1, SPR = 1, Sum = 6, SUM = 6, FAL = 9, Fal = 9, Aut = 9, AUT = 9, Win = 12, WIN = 12, 
            FALL = 9, Fall = 9,
        };
        static Dictionary<int, Dictionary<string, int>> words = new Dictionary<int, Dictionary<string, int>>();       //月度词频<月份，<词，词频>>
        static Dictionary<int, int> years = new Dictionary<int, int>();                 //统计各年份的文献数

        static List<string> stop = new List<string> {
            " ", "", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
            "a", "an", "be", "is", "am", "are", "were", "was", "the", "this", "that", "those", "these", "have", "has", "had",
            "can", "could", "may", "might", "he", "she", "it", "should", "to", "in", "for", "if", "while", "at", "who", "where",
            "what", "and", "with", "or", "after", "of", "which", "been", "do", "done", "we", "our", "also", "more", "less", "their",
            "them", "not", "on", "as", "by", "than", "however", "but", "then", "both", "more", "less", "between", "its", "all", "no",
            "other", "from",
            "A", "An", "Be", "Is", "Am", "Are", "Were", "Was", "The", "This", "That", "Those", "These", "Have", "Has", "Had", "Can", "Could",
            "May", "Might", "He", "She", "It", "Should", "To", "In", "For", "If", "While", "At", "Who", "Where", "What", "And", "On",
            "With", "Or", "After", "Of", "Which", "Been", "Do", "Done", "Our", "Also", "More", "Less", "Their", "We", "Them", "Not", "As",
            "By", "Than", "However", "But", "Then", "Both", "More", "Less", "Between", "Its", "All", "No", "Other", "From",
            };                                      //停顿词
        static Boolean end = false;                 //是否已超出搜索年限范围
        static Boolean sign = false;                //是否从存档中读取了数据


        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            /* 貌似只用修改sid */
            string url = "http://apps.webofknowledge.com/summary.do?product=UA&parentProduct=UA&search_mode=GeneralSearch&parentQid=&qid=1&SID=6FWiIi7eTkFddYitj2u&&update_back2search_link_param=yes&page=1";
            /**
             * 翻页功能
             */
            //如果存在记录，则加载数据
            if (File.Exists("../../break/page.txt"))
            {
                page = int.Parse(File.ReadAllText("../../break/page.txt"));

                var key = File.ReadAllLines("../../break/years_key.txt");
                var value = File.ReadAllLines("../../break/years_value.txt");
                for (int i = 0; i < key.Length; i++)
                    years.Add(int.Parse(key[i]), int.Parse(value[i]));

                key = File.ReadAllLines("../../break/words_key.txt");
                value = File.ReadAllLines("../../break/words_value.txt");
                for (int i = 0; i < key.Length; i++)
                {
                    var dict = value[i].Split(' ');
                    var w = new Dictionary<string, int>();
                    for (int j = 0; j < dict.Length - 1; j += 2)
                    {
                        w.Add(dict[j], int.Parse(dict[j + 1]));
                    }
                    words.Add(int.Parse(key[i]), w);
                }
                sign = true;
            }

            Regex re = new Regex(@"page=([\s\S]*?)$");
            while (!end)
            {
                string next_page = string.Format("page={0}", page);
                url = re.Replace(url, next_page);

                sign = false;
                bool cancel = GetSource(url);         //获取该页的论文链接
                if (!cancel)                          //在正常爬取的情况下才翻页
                    page++;
                else
                {
                    Console.WriteLine("\n\n\n查询失败！\n\n\n");
                    return;
                }
                if (!sign && page % 10 == 0)
                {
                    CallBack();         //进行一次数据存储
                }

                System.Threading.Thread.Sleep(500);         //避免频繁取样导致IP很快被封
            }
            Console.WriteLine("\n\n\n统计完成！\n\n\n");
            System.Diagnostics.Process.Start("../../Key.html");
        }
        //获取网页内容
        public static HtmlElement GetBody(string url)
        {
            WebBrowser web = new WebBrowser();
            web.ScriptErrorsSuppressed = true;

            web.Navigate(url);
            while (web.ReadyState != WebBrowserReadyState.Complete)
            {
                Application.DoEvents();
                //System.Threading.Thread.Sleep(500);
            }
            var res = web.Document.Body;
            
            web.Dispose();
            return res;
        }
        //获取本页论文链接同时记频并生成词云
        public static bool GetSource(string url)
        {
            bool cancel = false;                //爬取时间过长可能会被屏蔽，此时应发出提醒并停止
            List<string> res = new List<string>();
            var ele = GetBody(url);

            string pattern = @"class=""smallV110 snowplow-full-record"" href=""([\s\S]*?)""";
            Regex re = new Regex(pattern);

            string pattern2 = @"<SPAN class=label>出版年: ‏</SPAN><SPAN class=data_bold>[\s\S]*?>([\s\S]*?)</";
            Regex re2 = new Regex(pattern2);

            MatchCollection m = re.Matches(ele.InnerHtml);
            MatchCollection m2 = re2.Matches(ele.InnerHtml);

            int fore_month = 0;
            int range = m.Count < m2.Count? m.Count : m2.Count;
            if (range == 0)
            {
                cancel = true;
                return cancel;
            }
            for (int i = 0; i < range; i++)
            {
                int year = 0, month = 0;
                //Console.WriteLine(m2[i].Groups[1].ToString());            //test
                string[] date = m2[i].Groups[1].ToString().Split('-', ' ', '/', ',');
                foreach (var s in date)
                {
                    if (s.Length == 4)
                    {
                        if (!int.TryParse(s, out year))
                        {
                            months mm;
                            if (Enum.TryParse(s, out mm))             //对于四位的月份表示的处理
                                month = (int)mm;
                        }
                        //Console.WriteLine("年份：" + year);            //test
                    }
                    else if (s.Length == 3)
                    {
                        month = (int)Enum.Parse(typeof(months), s);
                        //Console.WriteLine("月份：" + month);           //test
                    }
                }
                
                //先统计月度
                if (!words.ContainsKey(month) && res.Count != 0)
                {
                    foreach (var l in res)      //！！！
                    {
                        if(!Abstract(l, fore_month))
                            return false;
                    }
                    res.Clear();
                }

                if (year >= 2010)
                {
                    string filter = m[i].Groups[1].ToString();
                    filter = filter.Replace("amp;", "");
                    //Console.WriteLine(filter);              //test
                    res.Add(filter);
                }
                else
                {
                    end = true;
                    if (years.ContainsKey(year + 1))
                    {
                        if (fore_month == month)         //隔年发的情况（2021.2->2020.2）
                        {
                            foreach (var l in res)      //！！！
                            {
                                if (Abstract(l, month))
                                    return false;
                            }
                            res.Clear();
                        }
                        Cloud(year + 1);        //生成年度、季度词云
                        words.Clear();
                    }
                    return false;
                }

                if (!years.ContainsKey(year))
                {//初始化
                    years.Add(year, 1);
                    
                    if(years.ContainsKey(year + 1))
                    {
                        if(fore_month == month)         //隔年发的情况（2021.2->2020.2）
                        {
                            foreach (var l in res)      //！！！
                            {
                                if (Abstract(l, month))
                                    return false;
                            }
                            res.Clear();
                        }
                        Cloud(year + 1);        //生成年度、季度词云
                        words.Clear();
                    }

                }
                else
                    years[year]++;                  //可能存在异常!!!!!!

                fore_month = month;
            }
            return cancel;
        }

        //解析摘要的内容
        public static Boolean Abstract(string url, int month)
        {
            var ele = GetBody(url);
            var coll = ele.GetElementsByTagName("div");
            if (coll.Count == 0)
                return false;
            //
            var seg = new JiebaSegmenter();
            //seg.AddWord("gastric cancer");

            //
            foreach (HtmlElement v in coll)
            {
                if (v.GetAttribute("className").Equals("block-record-info"))
                {
                    if (v.InnerText.Contains("摘要"))     //有的文献不提供摘要
                    {
                        //Console.WriteLine(v.InnerText);
                        string text = v.InnerText;
                        text = Regex.Replace(text, @"[^\w\s]", "");
                        text = text.Replace("\r", "").Replace("\n", "");        //删除文本中的空行
                        //Console.WriteLine(text);
                        var group = seg.Cut(text).ToList();
                        group.Remove("摘要");
                        foreach (var s in group)                                //统计词频
                        {
                            var ss = s.ToUpper();
                            if (!stop.Contains(s))                              //排除stop包含的停顿词
                            {
                                if (!words.ContainsKey(month))
                                    words.Add(month, new Dictionary<string, int> { { ss, 1 } });
                                else if (!words[month].ContainsKey(ss))
                                    words[month].Add(ss, 1);
                                else
                                    words[month][ss]++;
                            }
                        }
                        //test
                        break;
                    }
                }
            }
            return true;
            //...
        }

        //生成词云图片(根据参数大小判断是月度词云还是季度和年度词云，季度年度同时生成)
        public static void Cloud(int year)
        {
            System.Drawing.Image img;
            filter(3);                                                          //月度级过滤
            for (int time = 1; time < 13; time++)
            {
                if (words.ContainsKey(time))
                {
                    var wc = new WordCloud.WordCloud(800, 500);
                    img = wc.Draw(words[time].Keys.ToList(), words[time].Values.ToList());
                    img.Save("../../images/c" + year + "--" + time + ".cdr");
                }
                
            }

            //生成各季度词云
            filter(years[year] / 3000);                            //季度级过滤

            Dictionary<int, List<string>> keys = new Dictionary<int, List<string>>();
            Dictionary<int, List<int>> values = new Dictionary<int, List<int>>();

            foreach (var v in words.Keys)
            {
                if (v == 0)     //含有某些未标注月份的文献(不参与季度处理)
                    continue;
                int season = (v - 1) / 3;           

                if (keys.ContainsKey(season))
                    keys[season].AddRange(words[v].Keys.ToList());
                else
                    keys.Add(season, words[v].Keys.ToList());

                if (values.ContainsKey(season))
                    values[season].AddRange(words[v].Values.ToList());
                else
                    values.Add(season, words[v].Values.ToList());
            }
            for(int i = 0; i < keys.Count; i++)
            {
                var wc = new WordCloud.WordCloud(800, 500);         //创建一个WorldCloud只能使用一次？！
                img = wc.Draw(keys[i], values[i]);
                img.Save("../../images/b" + year + "-" + i + ".cdr");
            }
            keys.Clear(); values.Clear();                           //释放缓存

            List<string> key = new List<string>();
            List<int> value = new List<int>();

            //删除某些较小数据以免数据过大
            var _keys = words.Keys;
            filter(years[year] / 1000);                       //年度级过滤
            foreach (var k in _keys)
            {
                key.AddRange(words[k].Keys.ToList());
                value.AddRange(words[k].Values.ToList());
            }
            //生成年度词云
            var wcn = new WordCloud.WordCloud(800, 500);
            img = wcn.Draw(key, value);
            img.Save("../../images/a" + year + ".cdr");              //此图片格式为 a+年份、a+年份月份

        }

        //数据过滤 weight表示权重
        public static void filter(int weight)
        {
            var keys = words.Keys;
            foreach (var k in keys)
            {
                //foreach中不能对遍历的数据进行修改（增删改）操作，会破坏原数据下标结构，转换为list可解决该问题
                foreach (var w in words[k].Keys.ToList())       
                {
                    if (words[k][w] < weight)
                        words[k].Remove(w);
                }
            }
        }

        //从后向前找可行位置(注意溢出)
        public static int Vaild(int[] l, int now)
        {
            now--;
            int index = 0;
            while (0 < now)
            {
                if(0 != l[now])
                {
                    index = now;
                    break;
                }
                now--;
            }
            return index;
        }

        //回调函数，保存一次数据，便于多次间断采集数据
        public static void CallBack()
        {
            List<string> key = new List<string>();
            List<string> value = new List<string>();

            File.WriteAllText("../../break/page.txt", page.ToString());             //设置断点页数

            foreach(var v in years.Keys)
            {
                key.Add(v.ToString());
                value.Add(years[v].ToString());
            }
            File.WriteAllLines("../../break/years_key.txt", key);
            File.WriteAllLines("../../break/years_value.txt", value);
            key.Clear();value.Clear();

            foreach (var v in words.Keys)
            {
                key.Add(v.ToString());
                string vu = "";
                foreach (var l in words[v])
                {
                    vu += l.Key + " " + l.Value.ToString() + " ";
                }
                value.Add(vu);
            }
            File.WriteAllLines("../../break/words_key.txt", key);
            File.WriteAllLines("../../break/words_value.txt", value);

        }

    }

}
