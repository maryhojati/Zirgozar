using bot1.Types;
using GameBotService;
using log4net;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Transactions;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineKeyboardButtons;

namespace bot1
{
    public enum UserState
    {
        Greeting,
        ChooseLocation,
        AskQuestion,
        Answering,
        EndOfAllLocations,
        LookingForHole,
        EndGame
    }

    public class TelegramServiceClient
    {
        
#if DEBUG
        private readonly TelegramBotClient Bot = new TelegramBotClient("604640034:AAELCl****F9ePCxt7u******8UUYP5esU");
#else
        private readonly TelegramBotClient Bot = new TelegramBotClient("579413174:AAHSy-****QTTj7l**************yjLVA");
#endif
        private static readonly ILog logger = LogManager.GetLogger(typeof(TelegramServiceClient));

        private TelegramServiceClient()
        {
            logger.Info("TelegramServiceClient Constructor");
        }

        private static TelegramServiceClient _Instance = null;

        public static TelegramServiceClient Instance
        {
            get
            {
                if (_Instance == null)
                    _Instance = new TelegramServiceClient();
                return _Instance;
            }
        }

        public void Start()
        {
            try
            {
                using (var db = new ApplicationDbContext())
                {
                    db.Quenstions.Count();
                }
                logger.Info("bot service is starting");
                Bot.OnMessage += Bot_OnMessage;
                Bot.OnCallbackQuery += Bot_OnCallbackQuery;
                Bot.OnMessageEdited += Bot_OnMessage;
                Bot.StartReceiving();
                logger.Info("bot service started");                
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }
        }

        private UserState GetState(long ChatId)
        {
            using (var db = new ApplicationDbContext())
            {
                if (!db.Users.Any(a => a.ChatID == ChatId))
                    return UserState.Greeting;
                else if (!db.UserQuestions.Any(a => a.ChatID == ChatId)) //for first time
                    return UserState.ChooseLocation;
                else if (db.UserQuestions.Any(a => a.AnsTime == null && a.ChatID == ChatId))
                    return UserState.Answering;
                //else if (db.Quenstions.Any(a => db.UserQuestions.Any(b => b.Question.LocationCode == a.LocationCode &&
                //(b.QuestionID != a.QuestionID || (b.QuestionID == a.QuestionID && b.IsCorrectAnswer ==false)))))
                else if (db.Quenstions.Any(a =>
                                            (db.UserQuestions.Any(b => a.LocationCode == b.Question.LocationCode && b.ChatID == ChatId) &&
                                            !db.UserQuestions.Any(b => b.QuestionID == a.QuestionID && b.IsCorrectAnswer == true && b.ChatID == ChatId))
                                            )
                        )
                    return UserState.AskQuestion;
                else if (db.Quenstions.Any(a =>
                                            !db.UserQuestions.Any(b => b.QuestionID == a.QuestionID && b.IsCorrectAnswer == true && b.ChatID == ChatId)
                                            )
                         )
                    return UserState.ChooseLocation;
                else
                {
                    var user = db.Users.Find(ChatId);
                    if (!user.LookingForHole && user.EndTime == null)
                        return UserState.EndOfAllLocations;
                    else if (user.LookingForHole && user.EndTime == null)
                        return UserState.LookingForHole;
                    else if (user.EndTime != null)
                        return UserState.EndGame;
                    else
                        throw new Exception($"unhandled situation for uesr {user.ChatID}");
                }
            }
        }

        private void Bot_OnCallbackQuery(object sender, Telegram.Bot.Args.CallbackQueryEventArgs e)
        {
            try
            {
                logger.Debug($"{e.CallbackQuery.From.Username} is pressing button : {e.CallbackQuery.Data}");
                //if (e.CallbackQuery.Message.Date >= new DateTime(2018, 7, 02, 14, 31, 00) && e.CallbackQuery.Message.Date <= StartDate)
                //{
                //    logger.Info("the message is ignored");
                //    return;
                //}
                using (var db = new ApplicationDbContext())
                {
                    var state = GetState(e.CallbackQuery.Message.Chat.Id);
                    var user = db.Users.First(a => e.CallbackQuery.Message.Chat.Id == a.ChatID);
                    if (state == UserState.Greeting)
                    {
                        try
                        {
                            var replyMarkup = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new InlineKeyboardButton[] { });
                            var r = Bot.EditMessageReplyMarkupAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId, replyMarkup: replyMarkup).Result;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex.Message, ex);
                        }
                        SayGreeting(e.CallbackQuery.Message);
                        SendChooseLocation(e.CallbackQuery.Message.Chat.Id);
                        return;
                    }
                    if (state == UserState.ChooseLocation || user.PrefLocation == -1)
                    {
                        user.PrefLocation = int.Parse(e.CallbackQuery.Data);
                        var unAnsweredQuestions = db.UserQuestions.Where(a => a.AnsTime == null && a.ChatID == user.ChatID);
                        db.UserQuestions.RemoveRange(unAnsweredQuestions);
                        db.SaveChanges();
                        AskQuestion(user.ChatID);
                    }
                    if(user.Device== Device.Unknown)
                    {
                        ProcessEndGame(e.CallbackQuery.Message.Chat.Id, null, e.CallbackQuery);
                    }
                }
                {
                    var replyMarkup = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new InlineKeyboardButton[] { });
                    var r = Bot.EditMessageReplyMarkupAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId, replyMarkup: replyMarkup).Result;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }
        }

        public void Stop()
        {
            Bot.StopReceiving();
        }
        private DateTime StartDate = DateTime.UtcNow;
        private void Bot_OnMessage(object sender, Telegram.Bot.Args.MessageEventArgs e)
        {
            try
            {
                logger.Debug($"usser {e.Message.From.Username} with chatId:{e.Message.Chat.Id} is sending message : {e.Message.Text} with message Id : {e.Message.MessageId}");
                //if(e.Message.Date.ToUniversalTime()< StartDate )
                //{
                //    logger.Info("the message is ignored");
                //    return;
                //}
                e.Message.Text = e.Message.Text == null ? e.Message.Text : e.Message.Text.ToEngslishNums().Trim();
                var userState = GetState(e.Message.Chat.Id);
                if (userState == UserState.Greeting)
                {
                    SayGreeting(e.Message);
                    SendChooseLocation(e.Message.Chat.Id);
                    return;
                }
                if (e.Message.Text == "/choose_another_detective" && userState != UserState.EndGame)
                {
                    PrepareChangeLocation(e.Message.Chat.Id);
                    return;
                }
                else if (e.Message.Text == "/help" && userState != UserState.EndGame)
                {
                    SendHelp(e.Message.Chat.Id);
                    return;
                }
                switch (userState)
                {
                    case UserState.ChooseLocation:
                        SendChooseLocation(e.Message.Chat.Id);
                        break;

                    case UserState.AskQuestion:
                        AskQuestion(e.Message.Chat.Id);
                        break;

                    case UserState.Answering:
                        ProcessAnswer(e);
                        break;

                    case UserState.EndOfAllLocations:                        
                        AskTheHoleQuestion(e.Message.Chat.Id);
                        break;

                    case UserState.LookingForHole:
                        ProcessHoleAnswer(e.Message);
                        break;

                    case UserState.EndGame:
                        ProcessEndGame(e.Message.Chat.Id, e.Message);
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }
        }

        private void SendHelp(long id)
        {
            try
            {
                using (var db = new ApplicationDbContext())
                {
                    var question = db.UserQuestions.Where(a => a.ChatID == id && a.AnsTime == null).Select(a => a.Question.Clue).FirstOrDefault();
                    if (question != null)
                    {
                        var result = Bot.SendTextMessageAsync(id, question).Result;
                    }
                    else
                    {
                        var state = GetState(id);
                        string helpStr = "اینجا راهنمایی ندارم";
                        if (state == UserState.LookingForHole)
                        {
                            helpStr = @"کافه مایاک راه سنگ را نشانتان می‌دهد. 
لابی فنی پایین.";
                        }
                        var result = Bot.SendTextMessageAsync(id, helpStr).Result;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
                return;
            }
        }

        private void PrepareChangeLocation(long id)
        {
            try
            {
                logger.Debug($"preparing changeLocation for user : {id}");
                using (var scope = new TransactionScope(TransactionScopeOption.Required))
                {
                    using (var db = new ApplicationDbContext())
                    {
                        var user = db.Users.Where(a => a.ChatID == id).First();
                        user.PrefLocation = -1;
                        var unAnsweredQuestions = db.UserQuestions.Where(a => a.AnsTime == null && a.ChatID == user.ChatID);
                        db.UserQuestions.RemoveRange(unAnsweredQuestions);
                        db.SaveChanges();
                        SendChooseLocation(id);
                    }
                    scope.Complete();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
                return;
            }
        }

        private bool IsHoleFound(long chatId)
        {
            logger.Debug($"{chatId} has found the hole");
            using (var db = new ApplicationDbContext())
            {
                var user = db.Users.First(a => a.ChatID == chatId);
                return user.EndTime != null;
            }
        }

        private bool CheckHoleLocation(double latitude, double longitude)
        {
            var distance = Measure(35.701667, 51.394444, latitude, longitude);
            logger.Debug($"checking hole distance {latitude}, {longitude} with distance : {distance}");
            return distance <= 15;
        }

        private bool? IsAnswerCorrect(Question question, Message message, out int numberOfFailers)
        {
            try
            {
                logger.Debug($"checking answer correction for user : {message.Chat.Id}");
                using (var db = new ApplicationDbContext())
                {
                    numberOfFailers = db.UserQuestions
                        .Where(a => a.ChatID == message.Chat.Id && a.IsCorrectAnswer == false && a.QuestionID==question.QuestionID)
                        .Count();
                    if (!question.IsAnswerALocation)
                    {
                        //var qs = db.UserQuestions.Where(a => a.ChatID == message.Chat.Id && a.QuestionID == question.QuestionID).First();
                        if (question.AnsInt == int.Parse(message.Text))
                        {
                            return true;
                        }
                        else
                            numberOfFailers++;
                        return false;
                    }
                    else
                    {
                        double Longitude = 0;
                        double Latitude = 0;
                        if (message.Location == null)
                        {
                            var latlong = message.Text.ToLatLong();
                            Latitude = latlong.Item1;
                            Longitude = latlong.Item2;
                        }
                        else
                        {
                            Latitude = message.Location.Latitude;
                            Longitude = message.Location.Longitude;
                        }

                        var distance = Measure(question.AnsLoc.ToLatLong().Item1, question.AnsLoc.ToLatLong().Item2,
                            Latitude, Longitude);
                        if (distance <= 20)
                        {
                            return true;
                        }
                        else
                        {
                            numberOfFailers++;
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
                numberOfFailers = 0;
                return false;
            }
        }

        private double Measure(double lat1, double lon1, double lat2, double lon2)
        {
            //double distance;
            var R = 6378.137; // Radius of earth in KM

            var dLat = lat2 * Math.PI / 180 - lat1 * Math.PI / 180;
            var dLon = lon2 * Math.PI / 180 - lon1 * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            var d = R * c;
            return d * 1000;
        }

        private void ProcessEndGame(long id, Message message = null, CallbackQuery callbackQuery = null)
        {
            try
            {
                logger.Debug($"processing ProcessEndGame for user:{id}");
                Types.User user = null;
                using (var db = new ApplicationDbContext())
                {
                    user = db.Users.Find(id);
                    if (user.Device == null)
                    {
                        DisplayFinalVRApp(id);
                    }
                    else if (user.Device == Device.Unknown)
                    {
                        user.Device = (Device)(int.Parse(callbackQuery.Data));
                        db.SaveChanges();
                        DisplayFinalVRApp(id);
                    }
                    else
                    {
                        DisplayFinalVRApp(id);
                    }
                }
                
                //DateTime fin = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }
        }

        private void AskTheHoleQuestion(long id)
        {
            using (var db = new ApplicationDbContext())
            {
                var fin_user = db.Users.Where(a => a.ChatID == id).First();
                var result1 = Bot.SendTextMessageAsync(id, @"اطلاعیه فوری
با پی بردن به تمامی اعداد مربوط به مکان پنهان شدن سنگ، سیستم های فوق ابعادی ریئس کل دنیاهای موازی در بعد سوم، ملقب به دراگوآ وید، به کار افتاده است. مطرح کردن این نکته ها در این مرحله چندان حائز اهمیت نیست؛ اما به ناچار برای شما مطرح می کنیم. شاید اگر مطلع نشوید، از سر لج و لجبازی پی سنگ گشتن را رها کنید و با بچه بازی و نادانی دو دنیا را به هیچ مبدل کنید.
1.	گابروآ ژید همون ناتانائیل دگاریا است، که برای مخفی کردن هویت خود به ناچار حافظه اش را دستکاری کرد و با قرص های، به ظاهر، ضد افسردگی که مصرف میکند این پدیده را تداوم میبخشد. لازم بود بتواند برای جلب اعتماد دراگوآ نه تنها از آزمون های بررسی ناخودآگاه رد شود و کم کم برگرده به حالت طبیعی اش و به طور نفوذی وار انجمن زیر و رو رو راهنمایی کنه. اما متاسفانه قرص هایی که مصرف میکند به واقع با کیفیت بودند و او هیچ وقت نتوانست برگردد و راهنمایی کردن شما سه بعدی های دست چندم برای این که سنگ را برایمان پیدا کنید به پروژه ی پردردسری تبدیل شد.
2.	جزئیات بیشتری هم هستند که مطرح کردنشان فعلا هیچ اولویتی ندارد. چون هم الآن دراگوآ با سرعتِ بالای سرعتِ نورِ ووگاتی بیرونِ بین ابعادیاش به جهان شما نزدیک میشود که سنگ را بردارد.
3.	نقشه های که بعد از  طی مراحل هر دانشکده به دست آورده ای را کنار هم بچین تا بتوانی بفهمی این اعدادی را چه طور باید Bکنار هم بچینی تا محل سنگ را کشف کنی.
پی نوشت: به یاد داشته باش همیشه میتوانی به رستوران های نقطه اتصال بین ابعادی سر بزنی، آن جا همیشه موجودات ابعاد بالاتر هستند که برای سرگرمی هم که شده راهنمایی های سطح بالا برایت کنار میگذارند.

ارادتمند،
آندو ژیله
جایگزین دبیر کل انجمن زیر و رو

").Result;

                var result2 = Bot.SendTextMessageAsync(id, "مختصات محل سنگ را وارد کنید. پاسخ اشتباه، یک ساعت شما را از رسیدن به محل سنگ دور می‌کند.").Result;
                fin_user.LookingForHole = true;
                db.SaveChanges();
            }
        }

        private void ProcessHoleAnswer(Message message)
        {
            using (var db = new ApplicationDbContext())
            {
                var id = message.Chat.Id;
                var fin_user = db.Users.Where(a => a.ChatID == id).First();
                {
                    double Longitude = 0;
                    double Latitude = 0;
                    try
                    {
                        if (message.Location == null)
                        {
                            var latlong = message.Text.ToLatLong();
                            Latitude = latlong.Item1;
                            Longitude = latlong.Item2;
                        }
                        else
                        {
                            Latitude = message.Location.Latitude;
                            Longitude = message.Location.Longitude;
                        }
                    }
                    catch
                    {                        
                    }

                    if (CheckHoleStringLocation(message) || CheckHoleLocation(Latitude, Longitude))
                    {
                        fin_user.EndTime = DateTime.UtcNow;
                        db.SaveChanges();
                        ProcessEndGame(id, message);
                        CalculateScore(id);
                    }
                    else
                    {
                        var ss = Bot.SendTextMessageAsync(id, "متاسفانه مختصات حفره صحیح نیست و یک ساعت از رسیدن به سنگ دور شدید").Result;
                        var result2 = Bot.SendTextMessageAsync(id, "مختصات محل سنگ را وارد کنید. پاسخ اشتباه، یک ساعت شما را از رسیدن به محل سنگ دور می‌کند.").Result;
                        fin_user.HoleFailed++;
                        db.SaveChanges();
                    }
                }
            }
        }

        private bool CheckHoleStringLocation(Message message)
        {
            try
            {
                logger.Info($"checking string hole");
                var str = new List<string> { "354206512340", "512340354206" };
                var s = message.Text.Replace(" ", "")
                                    .Replace("'", "")
                                    .Replace("\"", "")
                                    .Replace(",", "")
                                    .ToLower()
                                    .Replace("n", "")
                                    .Replace("e", "")
                                    .Trim();
                logger.Debug($"checking string: {s} ");
                return str.Any(a => a == s);
            }
            catch 
            {
                return false;
            }
        }

        private void DisplayFinalVRApp(long id)
        {
            using (var db = new ApplicationDbContext())
            {
                var user = db.Users.Find(id);
                if (user.Device == null)
                {
                    user.Device = Types.Device.Unknown;
                    var buttons = new List<InlineKeyboardButton>();
                    foreach (Device loc in Enum.GetValues(typeof(Device)))
                    {
                        if (loc == Device.Unknown)
                            continue;
                        var b = Telegram.Bot.Types.InlineKeyboardButtons.InlineKeyboardButton.WithCallbackData(loc.ToString(), ((int)loc).ToString());
                        buttons.Add(b);
                    }

                    var replyMarkup = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(buttons.ToArray());
                    var rr = Bot.SendTextMessageAsync(id, "پیغام ِ فوری:  تنها با «زیرو اَپ» می توانید سنگ را  ببینید!").Result;
                    var result = Bot.SendTextMessageAsync(id, "سیستم عامل گوشی خود رو انتخاب کنید", replyMarkup: replyMarkup);
                    db.SaveChanges();
                }
                else
                {
                    if(user.Device==Device.iOS)
                    {
                        var result1 = Bot.ForwardMessageAsync(id, 105111977,  12307).Result;
                    }
                    else if(user.Device==Device.Android)
                    {
                        var result2 = Bot.SendTextMessageAsync(id, @"لینک زیرو اپ در کافه بازار:
https://cafebazaar.ir/app/com.zero.app/?l=fa").Result;
                    }
                    //var result = Bot.ForwardMessageAsync(id, 105111977, user.Device == Device.Android? 9558:12307).Result;
                    Task.Delay(500).Wait();
                    var s3 = Bot.SendTextMessageAsync(id, @"
بعد از دیتکت کردن حفره لاوگاردن توسط زیرو اپ، با عبیدینوس تماس بگیرید:
@obeideinos").Result;
                    Task.Delay(300).Wait();
                    var ss = Bot.SendTextMessageAsync(id, "https://goo.gl/forms/UOc5vRn2RDymiXOy1").Result;
                }                
            }
        }

        private void CalculateScore(long chatId)
        {
            using (var db = new ApplicationDbContext())
            {
                var user = db.Users.First(A => A.ChatID == chatId);
                if (user.EndTime == null)
                    throw new Exception("end time is null in score calculations");
                var startDate = user.StartTime;
                var endDate = (DateTime)user.EndTime;
                var numberOfFails = db.UserQuestions.Count(a => a.ChatID == chatId && a.IsCorrectAnswer == false);
                var holeFailed = user.HoleFailed; //تعداد دفعاتی که مختصات سنگ را اشتباه وارد کرده است
                var totalHoursSpend = (endDate - startDate).TotalHours;
                //throw new NotImplementedException("مریم اینجا همه چیز برای محاسبه امتیاز رو داری  و اگه نیازه پیامی بدی بنویسش و خلاصه کاراش رو انجام بده اگرم نیاز نیست چیزی به کاربر نشون بدی که خب هیچی");
                user.Score = 0;
                db.SaveChanges();
            }
        }

        private void AskQuestion(long id)
        {
            //ask any question that has wrong or none answer
            try
            {
                logger.Debug($"asking question for {id}");
                using (var scope = new TransactionScope(TransactionScopeOption.Required))
                {
                    using (var db = new ApplicationDbContext())
                    {
                        var prefLocation = db.Users.Where(a => a.ChatID == id).Select(a => a.PrefLocation).First();
                        bool chooseNewLocation = false;
                        if (prefLocation == -1)
                            throw new Exception("pref location is sub zero");
                        Question nxt_question = null;
                        if (prefLocation != null)
                        {
                            nxt_question = db.Quenstions.Where(a =>
                                            (int)a.LocationCode == (int)prefLocation &&
                                            !db.UserQuestions.Any(c => c.QuestionID == a.QuestionID && c.IsCorrectAnswer == true && c.ChatID == id))
                                            .OrderBy(a => a.Order).FirstOrDefault();

                            if (nxt_question == null)
                            {
                                var user = db.Users.First(a => a.ChatID == id);
                                user.PrefLocation = -1;
                                chooseNewLocation = true;
                                db.SaveChanges();
                            }
                        }

                        if (nxt_question == null && !chooseNewLocation)
                        {
                            nxt_question = db.Quenstions.Where(a => db.UserQuestions.Any(b => b.Question.LocationCode == a.LocationCode && b.ChatID == id)
                                                                            && !db.UserQuestions.Any(c => c.QuestionID == a.QuestionID &&
                                                                            c.IsCorrectAnswer == true && c.ChatID == id)
                                                                            )
                                .OrderBy(a => a.Order).FirstOrDefault();
                        }

                        if (nxt_question == null)
                        {
                            UserState userState = GetState(id);
                            if (userState == UserState.AskQuestion && chooseNewLocation)
                                userState = UserState.ChooseLocation;
                            //var us = db.Users.Where(a => a.ChatID == id).First();
                            //us.PrefLocation = (int)nxt_question.LocationCode;
                            db.SaveChanges();
                            switch (userState)
                            {
                                case UserState.ChooseLocation:
                                    SendLocationCompletionMessage(id);
                                    SendChooseLocation(id);
                                    break;

                                case UserState.EndGame:
                                    SendAllLocationsCompletedMessage(id);
                                    ProcessEndGame(id, null);
                                    break;

                                case UserState.AskQuestion:
                                    if (prefLocation == -1)
                                        SendChooseLocation(id);
                                    else
                                        throw new Exception("Unexpected situation");

                                    break;

                                default:
                                    break;
                            }
                            scope.Complete();
                            return;
                        }

                        var asked_question = new User_Questions
                        {
                            ChatID = id,
                            AskTime = DateTime.UtcNow,
                            QuestionID = nxt_question.QuestionID,
                        };

                        db.UserQuestions.Add(asked_question);
                        db.SaveChanges();
                        var result = Bot.SendTextMessageAsync(id, nxt_question.Content, Telegram.Bot.Types.Enums.ParseMode.Html).Result;
                    }
                    scope.Complete();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }
        }

        private void SendLocationCompletionMessage(long id)
        {
            try
            {
                logger.Debug($"sending locationCompleteMessage for user : {id}");
                using (var db = new ApplicationDbContext())

                {
                    var result = Bot.SendTextMessageAsync(id, @"اطلاعیه معمولی
زیاد خوشحال نشو، هنوز هیچ اتفاق خاصی نیافتاده‌است. دانشکده‌ی بعدی را انتخاب‌کن و فراموش نکن همیشه تعدادی موجودات ابعاد بالاتر هستند که در کافه‌های بین‌ابعادی می‌چرخند و برای سرگرمی هم که شده راهنمایی‌های سطح بالا برایت کنار می‌گذارند.
", Telegram.Bot.Types.Enums.ParseMode.Html).Result;

                    var user = db.Users.Where(b => b.ChatID == id).First();
                    var locationCode = db.UserQuestions.Where(a => a.ChatID == id &&
                    a.IsCorrectAnswer == true).OrderByDescending(a => a.AnsTime).Select(a => a.Question.LocationCode).First();
                    
                        var msg = db.ComletionMessage.Where(a => a.LocationCode == locationCode).First();
                        var FileUrl = System.IO.Path.Combine(ConfigurationManager.AppSettings["PicturesAddress"], msg.PlanAddress.Trim());
                        using (var stream = System.IO.File.Open(FileUrl, FileMode.Open))
                        {
                            FileToSend photo = new FileToSend();
                            photo.Content = stream;
                            photo.Filename = FileUrl.Split('\\').Last();

                            var result2 = Bot.SendPhotoAsync(id, photo, msg.LocationCode.ToString()).Result;
                        }
                    
                    //else
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }
        }

        private void SendAllLocationsCompletedMessage(long id)
        {
            //throw new NotImplementedException();
        }

        private bool CheckLocationCompletion(long id)
        {
            throw new NotImplementedException();
        }

        private bool ProcessAnswer(MessageEventArgs e)
        {
            try
            {
                logger.Debug($"processing answer for user : {e.Message.Chat.Id}");
                using (var db = new ApplicationDbContext())
                {
                    //var us_question = db.UserQuestions.Where(a => a.ChatID == e.Message.Chat.Id).OrderByDescending(a=>a.AnsTime).First();

                    var us_question = db.UserQuestions.Where(a => a.AnsTime == null && a.ChatID == e.Message.Chat.Id).First();
                    var question = us_question.Question;
                    //validating answer
                    if (question.IsAnswerALocation)
                    {
                        var regex = @"^([-+]?)([\d]{1,2})(((\.)(\d+))[\s]?)([,\s])(\s*)(([-+]?)([\d]{1,3})((\.)(\d+))?)";
                        if (e.Message.Location == null && !Regex.IsMatch(e.Message.Text, regex))
                        {
                            var result = Bot.SendTextMessageAsync(e.Message.Chat, "شما باید موقعیت مکانی ارسال کنید و یا مختصات جغرافیایی دقیق به صورت زیر ارسال کنید:\n51.498134 , -0.201755").Result;
                            return false;
                        }
                    }
                    else
                    {
                        var input = e.Message.Text.ToEngslishNums();
                        int num = 0;
                        if (!int.TryParse(input, out num))
                        {
                            var result = Bot.SendTextMessageAsync(e.Message.Chat, "فقط عدد وارد کنید").Result;
                            return false;
                        }
                    }//end of validation

                    int numberOfWrong = 0;
                    us_question.IsCorrectAnswer = IsAnswerCorrect(question, e.Message, out numberOfWrong);
                    if (us_question.IsCorrectAnswer == false)
                    {
                        string wrongStr;
                        switch (numberOfWrong)
                        {
                            case 1:
                                wrongStr = "درست نبود! دومین پاسخ اشتباه، سه ساعت شما را از رسیدن به محل سنگ دور می‌کند." +
                                    " برای استفاده از راهنمایی‌های موجودات ابعاد بالاتر دکمه راهنمایی را بزنید.";
                                break;

                            case 2:
                                wrongStr = "فکر می‌کنم اختلاف ابعادی کم‌کم واضح م" +
                                    "ی‌شود!سومین پاسخ اشتباه، شش ساعت شما را از رسیدن به محل سنگ دور می کند." +
                                    " برای استفاده از راهنمایی های موجودات ابعاد بالاتر دکمه راهنمایی را بزنید.";
                                break;

                            default:
                                wrongStr = "با دقت بیش‌تری به متن مراجعه کنید. اشتباه بعدی شش ساعت شما را ا" +
                                    "ز رسیدن به محل سنگ دور می‌کند. برای استفاده از راهنمایی‌های موجودات ابعاد بالاتر دکمه راهنمایی را بزنید.";
                                break;
                        }

                        var result = Bot.SendTextMessageAsync(e.Message.Chat.Id, wrongStr).Result;
                    }
                    else
                    {
                        string crrctStr = question.CompletedMessage;
                        if (!string.IsNullOrEmpty(crrctStr))
                        {
                            var result = Bot.SendTextMessageAsync(e.Message.Chat.Id, crrctStr, Telegram.Bot.Types.Enums.ParseMode.Html).Result;
                        }
                    }
                    us_question.AnsTime = DateTime.UtcNow;
                    if (question.IsAnswerALocation)
                    {
                        us_question.AnsLocation = e.Message.Location != null ? e.Message.Location.Latitude.ToString() + "," + e.Message.Location.Longitude.ToString() : e.Message.Text;
                    }
                    else
                    {
                        us_question.AnsInt = int.Parse(e.Message.Text);
                    }

                    db.SaveChanges();
                    var state = GetState(e.Message.Chat.Id);
                    if(state == UserState.EndOfAllLocations)
                    {
                        SendLocationCompletionMessage(e.Message.Chat.Id);
                        SendAllLocationsCompletedMessage(e.Message.Chat.Id);
                        AskTheHoleQuestion(e.Message.Chat.Id);
                    }
                    else
                    {
                        AskQuestion(e.Message.Chat.Id);
                    }
                    return us_question.IsCorrectAnswer == true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
                return false;
            }
        }

        private void SendChooseLocation(long id)
        {
            try
            {
                logger.Debug($"SendChooseLocation for user : {id}");
                using (var db = new ApplicationDbContext())
                {
                    /* var locs = db.Quenstions.Select(a => a.LocationCode).Where(b => db.UserQuestions.Where(c => c.ChatID == id && c.Question.LocationCode == b &&
                     db.UserQuestions.Any(d => d.ChatID == id && d.QuestionID == c.QuestionID && d.IsCorrectAnswer == true)));

     */
                    var remainingQuestions = db.Quenstions.Where(q => !db.UserQuestions.Any(u => u.QuestionID == q.QuestionID && u.ChatID == id && u.IsCorrectAnswer == true));

                    var locs = remainingQuestions.Select(a => a.LocationCode).Distinct();

                    var buttons = new List<InlineKeyboardButton>();
                    foreach (Locations loc in locs)
                    {
                        var b = Telegram.Bot.Types.InlineKeyboardButtons.InlineKeyboardButton.WithCallbackData(Question.GetPersionLocation(loc), ((int)loc).ToString());
                        buttons.Add(b);
                    }

                    var replyMarkup = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(buttons.ToArray());
                    var result = Bot.SendTextMessageAsync(chatId: id, text: "یکی از مکان‌های زیر رو انتخاب کن!", replyMarkup: replyMarkup).Result;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }
        }

        private void SayGreeting(Message message)
        {
            try
            {
                logger.Debug($"greeting for user : {message.Chat.Id}");
                using (var scope = new TransactionScope(TransactionScopeOption.Required))
                {
                    using (var db = new ApplicationDbContext())
                    {
                        var user = new Types.User
                        {
                            ChatID = message.Chat.Id,
                            UserName = message.From.Username,
                            StartTime = DateTime.UtcNow,
                        };
                        db.Users.Add(user);
                        db.SaveChanges();

                        var result = Bot.SendTextMessageAsync(message.Chat, @"اطلاعیه
بانک بین ابعادی سویفتس، در سوم اردیبهشت ۹۷، به یکی از تاریخ های کره ی زمین، سرقت یک قطعه از جواهرات نسبتا با ارزش خود را گزارش داده است.طی تحقیقاتی که تا به امروز در اختیار معاونین من قرار گرفته، سارق بعد از مخفی کردن شیءِ به سرقت رفته، و پس از تعقیب و گریز با مامورین پلیس بین ابعادی دچار سانحه شده و به کما رفته است.گزارش ها حاکی از آن است که سارق سنگ جواهر را در محدوده دانشگاه تهران پنهان کرده است.هیچ سرنخی از هویت او، انگیزه ی دزدیدن سنگ و نشانی از همدستان وی در دست نیست.هنوز مطلع نیستیم که چرا کسی به خاطر دزدین سنگی جان خود را به مخاطره افکند که سال ها در گاوصندوق این بانک بین ابعادی خاک می خورده، و تقریبا هیچ گونه ای از ابعاد مختلف، وجود چنین سنگی را دیگر به خاطر ندارد، در حالی که اشیا  ارزشمند تری در بانک نگهداری میشوند.با وجود اینکه جدا در تلاش هستم که اطلاعات بیشتری در اختیارتان قرار دهم تا سنگ مورد نظر را راحت تر پیدا کنید و زودتر بتوانید دو دستی تحویل نماینده من در بانک بدهید، متاسفانه نمی خواهم اطلاعاتی در اختیارتان بگذارم!قرص ساعت ۱۰ و ۱۰ دقیقه و ۱۰ ثانیه‌ام را همین حالا مصرف کردم و <a href='http://wiki.obeid.ir/index.php/%D8%A8%D8%B1_%D8%A7%D8%A8%D8%B1'>بَرْاَبر </a> هستم.لطفا مسئله را شخصی نکنید.

پی نوشت ۱: بانک از طرف شخص من جایزه ویژه ای برای فردی که سنگ را پیدا کند و تحویل بدهد تعیین کرده است.نمی دانم چرا می خواهم تا این حد هزینه کنم برای پیدا شدن سنگی که هیچ نمیدانم به چه دردی میخورد!
پی نوشت ۲: کاراگاه های خصوصی من در دانشکده های مختلف سرنخ های بسیار ناچیزی پیدا کردند که ترجیح میدادم برایتان ارسال نکنم، چون بسیار بی ارزش اند.اما به این دلیل که برای این کارآگاه ها هزینه ی زیادی کرده ام، حیف است تحقیقاتِ هرچند بی فایده شان را همین طور بلااستفاده دور بریزم.از هردانشکده که بخواهید میتوانید شروع کنید.فعلا با انتخابتان باشید تا شاید از چیزی سر دربیاورید.من باید به استراحتِ بعد از قرص بپردازم.
ارادتمند؛

<a href='http://wiki.obeid.ir/index.php/%D9%88%D9%88%DA%AF%D8%A7%D8%AA%DB%8C_%D8%A8%D9%90%DB%8C%D8%B1%D9%88%D9%86'>گابروآ ژید </a>
مشاور ارشد کل دنیاهای موازی در بعد سوم
", Telegram.Bot.Types.Enums.ParseMode.Html).Result;

                        /*var result1 = Bot.SendTextMessageAsync(message.Chat, @"اطلاعیه فوری
گابروآ بالاخره قرص ساعت ۱۰ و ۱۰ دقیقه و ۱۰ ثانیه‌اش را خورد و <a href='http://wiki.obeid.ir/index.php/%D8%A8%D8%B1_%D8%A7%D8%A8%D8%B1'>بَرْاَبر </a>شد. کمی از شدت خشمش نسبت به کارآگاه‌های قبلی کم شد، و اجازه‌ی گشایش درگاه زیرگذر را صادر کرد.

<a href='http://wiki.obeid.ir/index.php/Obeideinos'>عبیدینوس </a>
به ضم عین و به کسر باء و دال", Telegram.Bot.Types.Enums.ParseMode.Html).Result;*/
                        var r = Bot.SendTextMessageAsync(message.Chat, @"اطلاعیه بین‌راهی

کافه‌های نقطه‌اتصال بین‌ابعادی:
در هر مرحله‌ از جستجو که هستید، هر زمانی واقعاً متوجه شدید که فوریتِ پیدا‌‌کردنِ سنگِ دزدیده‌شده چه‌قدر است، می‌توانید از این کافه‌ها استفاده‌کنید. از آن‌جایی که اتفاق قریب‌الوقوعی که در دنیای شما و دنیای موازی بیچاره‌ی دیگری خواهد‌افتاد برای موجودات ابعاد بالاتر به نوعی سرگرمی به‌حساب‌می‌آید و بر روند اتفاقات این ماجرا شرط‌بندی‌هایی انجام‌می‌دهند، چندین کافه‌ی بین‌ابعادی را به کافه‌های معمولی شما متصل کرده‌اند. در حالی‌که می‌توانند در این مراکز شما را تماشا کنند و اوقات فراغت خوبی را سپری کنند، بعضی از آن‌ها راهنمایی‌های برای افراد درگیر در ماجرا قرار می‌دهند که می‌تواند سرعت جلو رفتن در حل مسئله‌ی حیاتی دنیای شما و دنیای موازی‌تان را بالا ببرد.
پی‌نوشت: از این بابت که راهنمایی اشتباه گیرتان بیاید نگران نباشید، از جمله قوانین اصلی شرط‌بندی در این کافه‌های بین‌ابعادی این است که هیچ‌کس حق گمراه‌کردن افراد درگیر در ماجرا را ندارد. این قانون خیلی جدی توسط درستریس بازینه، دادستان کل دنیاهای موازی همه‌ی ابعاد پیگیری می‌شود و تا ایشان زنده هستند هیچ کس جرأت تخطی از این قانون را پیدا نخواهد‌کرد.

با گرم‌ترین احترامات،
… ").Result;

                        var r2 = Bot.SendTextMessageAsync(message.Chat, @"در هر زمانی از جستجو می‌توانی از دستورات /help و /choose_another_detective استفاده کنی. ").Result;
                        scope.Complete();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }
        }
    }
}