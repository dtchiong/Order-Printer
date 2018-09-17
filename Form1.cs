﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GmailQuickstart {

    public partial class Form1 : Form {

        public static BindingSource orderListBindingSrc = new BindingSource();
        public static MySortableBindingList<OrderContainer> OrderList = new MySortableBindingList<OrderContainer>();

        //the dictionary of orders where the key is the messageId, used to track order status
        public static Dictionary<string, OrderContainer> OrderTableByMsgId = new Dictionary<string, OrderContainer>();
        //the dictionary of orders where the key is the orderNum, used to track grubhub cancelled orders
        public static Dictionary<string, List<OrderContainer>> OrderTableByOrdNum = new Dictionary<string, List<OrderContainer>>();

        public static string CurrentForm = "Last Orders"; //there's a Form.ActiveForm

        public Form1() {
            InitializeComponent();

            //Initialize dgv columns and properties
            //Prevents columns from auto populating with OrderContainer fields. 
            //Need to set before setting datasource, or else columns get duplicated for some reason.
            dataGridView1.AutoGenerateColumns = false;
            orderListBindingSrc.DataSource = OrderList;
            dataGridView1.DataSource = orderListBindingSrc;
            orderListBindingSrc.Sort = "TimeReceivedTicks DESC"; //set to sort DESC on the Ticks property

            dataGridView1.Columns.Add(NewTextBoxCol("Service", "Service"));
            dataGridView1.Columns.Add(NewTextBoxCol("Name", "Name"));
            dataGridView1.Columns.Add(NewTextBoxCol("ItemCount", "Order Size")); 
            dataGridView1.Columns.Add(NewTextBoxCol("TimeReceived", "Time Received"));
            dataGridView1.Columns.Add(NewTextBoxCol("PickUpTime", "Pick-Up Time"));
            dataGridView1.Columns.Add(NewTextBoxCol("Status", "Order Status"));
            dataGridView1.Columns.Add(NewTextBoxCol("PrintStatus", "Print Status"));

            dataGridView1.Columns["Service"].FillWeight = 80;
            dataGridView1.Columns["Order Size"].FillWeight = 80;

            dataGridView1.Columns.Add(NewTextBoxCol("TimeReceivedTicks", "TIMERECEIVEDTICKS"));
            dataGridView1.Columns["TIMERECEIVEDTICKS"].Visible = false;

            LoadSideBarIcons();
        }

        /* This solves the degrading quality of images in the imagelist over compiles
         * by loading all the images from the resources at runtime
         */
        private void LoadSideBarIcons() {
            imageList1.ColorDepth = ColorDepth.Depth32Bit;
            imageList1.Images.Clear(); //Clears the images in the designer

            Bitmap homeImage = new Bitmap(Order_Parser.Properties.Resources.home_white2);
            Bitmap AnalyticsImage = new Bitmap(Order_Parser.Properties.Resources.bar_chart_white);
            Bitmap MenusImage = new Bitmap(Order_Parser.Properties.Resources.cutlery_white);
            Bitmap ActionsImage = new Bitmap(Order_Parser.Properties.Resources.error_advice_sign);
            Bitmap SettingsImage = new Bitmap(Order_Parser.Properties.Resources.settings_5_white);
            Bitmap AboutImage = new Bitmap(Order_Parser.Properties.Resources.information);

            imageList1.Images.Add(homeImage);
            imageList1.Images.Add(AnalyticsImage);
            imageList1.Images.Add(MenusImage);
            imageList1.Images.Add(ActionsImage);
            imageList1.Images.Add(SettingsImage);
            imageList1.Images.Add(AboutImage);
        }

        /* Returns a new DataGridViewColumn given the databinding propertyname, and the header name */
        DataGridViewColumn NewTextBoxCol(string propertyName, string headerName) {
            DataGridViewColumn col = new DataGridViewTextBoxColumn();
            col.DataPropertyName = propertyName;
            col.Name = headerName;
            return col;
        }

        /* Gets the order that is currently selected, if any, and then prints it, and sets the Print Status 
         * To protect from spam, we disable the button on click, and then only enable the button after the 
         * print status is set. Maybe use a timer that starts afer print status is set.
         */
        private void print_Click(object sender, EventArgs e) {
            button1.Enabled = false;
            button1.BackColor = ColorTranslator.FromHtml("#90979B");

            DataGridViewSelectedRowCollection selectedRows = dataGridView1.SelectedRows;
            
            if (selectedRows.Count > 0) {
                OrderContainer orderCon = (OrderContainer)selectedRows[0].DataBoundItem;
                if (orderCon == null) return; //incase there are no orders

                bool printed = PrinterUtility.PrintOrder(orderCon);
                if (printed) {
                    orderCon.PrintStatus = "Printed";
                }//else error messages are handled in the print utility

                //To force the form to update the print status value. 
                //dataGridView1.Refresh() maybe useful if using a "Print All" button
                orderListBindingSrc.ResetCurrentItem();
            }

            button1.Enabled = true;
            button1.BackColor = ColorTranslator.FromHtml("#4A5157");
        }

        /* Encapsulates an order with an OrderContainer, then add it to the order list */
        public void AddOrderToList(Order order, bool isAdjustedOrder) {
            OrderContainer orderCon = new OrderContainer(order);
            if (isAdjustedOrder) orderCon.Status = "Active(Adjusted)";

            OrderList.Add(orderCon); //Add the OrderContainer to the OrderList to update the UI list
            
            //there seems to be a bug where an existing messageId tries to be inserted again 
            try {
                OrderTableByMsgId.Add(order.MessageId, orderCon); //Insert the entry into the table for easily updating order status
            } catch (Exception e) {
                Debug.WriteLine(order.MessageId + " already inserted in OrderTable");
            }

            if (order.Service == "GrubHub") InsertToOrderNumTable(orderCon); //We insert the entry into the table with orderNum as the key

            //Update the Item List in the GUI to match the selected row 
            UpdateOrderUI();
            PlaySound(Program.NotificationSoundPath); //play the nofication sound
        }

        /* If the orderCon's orderNum already exists in the table, we add the orderCon to the list, else
         * we insert a new entry with the new orderCon inserted into the entry's list
         */
        public void InsertToOrderNumTable(OrderContainer orderCon) {
            List<OrderContainer> orderConList;
            if (OrderTableByOrdNum.TryGetValue(orderCon.order.OrderNumber, out orderConList)) {
                orderConList.Add(orderCon);
            } else {
                orderConList = new List<OrderContainer>();
                orderConList.Add(orderCon);
                OrderTableByOrdNum.Add(orderCon.order.OrderNumber, orderConList); //Insert the entry into the table for tracking grubhub cancelled orders
            }
        }

        /* Given the messageId, change the associated order's status to Ignore */
        public void ChangeStatusToAdjusted(string messageId) {
            OrderContainer orderCon;
            if (OrderTableByMsgId.TryGetValue(messageId, out orderCon)) {
                orderCon.Status = "Ignore(See adjusted)";
                //refresh the view so that the status in the UI is updated, 
                //would be more efficient if I had the current binded object to reset, instead of the entire view
                dataGridView1.Refresh(); 
            }
        }

        /* Given the orderNum, set the associated orders' status to Cancelled */
        public void SetOrderToCancelled(string orderNum) {
            List<OrderContainer> orderConList;
            bool cancelledOrder = false;
            if (OrderTableByOrdNum.TryGetValue(orderNum, out orderConList)) {
                foreach (var orderCon in orderConList) {
                    cancelledOrder = true;
                    orderCon.Status = "Cancelled";
                    dataGridView1.Refresh();
                }
            }
            //this prevents from playing sound if no orders in the list were actually cancelled
            if (cancelledOrder) PlaySound(Program.CancelledOrderSoundPath); 
        }

        //Plays the sound given the path of it
        private void PlaySound(string soundPath) {
            SoundPlayer sound = new SoundPlayer(soundPath);
            sound.Play();
        }

        /* Updates the UI to match the order in the selected row by
         * updating the Order Details and Item List
         */
        private void UpdateOrderUI() {
            DataGridViewSelectedRowCollection selectedRows = dataGridView1.SelectedRows;
            OrderContainer selectedRow = (OrderContainer)selectedRows[0].DataBoundItem;

            UpdateItemListUI(selectedRow);
            UpdateOrderDetailsUI(selectedRow);
            UpdateItemDetailsUI();
        }

        /* Updates the Item List in the UI to match the currently selected row */
        private void UpdateItemListUI(OrderContainer orderCon) {
            dataGridView2.DataSource = orderCon.order.ItemList;
        }
        
        /* Updates the fields of Order Details */
        private void UpdateOrderDetailsUI(OrderContainer orderCon) {
            Order order = orderCon.order;

            nameTextBox.Text = order.CustomerName;
            contactNumTextBox.Text = order.ContactNumber;
            orderNumTextBox.Text = order.OrderNumber;

            orderSizeTextBox.Text = order.OrderSize.ToString();
            if (order.NumOfDrinks > 0 && order.NumOfSnacks == 0) {
                orderSizeTextBox.Text += " (all Drinks)";
            }else if (order.NumOfSnacks > 0 && order.NumOfDrinks == 0) {
                orderSizeTextBox.Text += " (all Snacks)";
            }else if (order.NumOfDrinks > 0 && order.NumOfSnacks > 0) {
                orderSizeTextBox.Text += " (" + order.NumOfDrinks + " Drinks, " + order.NumOfSnacks + " Snacks)";
            }

            messageIdTextBox.Text = order.MessageId;
        }

        /* Updates the Item List UI to match the currently selected item in the Item List UI*/
        private void UpdateItemDetailsUI() {

            //Get the currectly selected row's item
            DataGridViewSelectedRowCollection selectedRows = dataGridView2.SelectedRows;
            Item item = (Item)selectedRows[0].DataBoundItem;
            
            textBoxQty.Text = item.Quantity.ToString();
            textBoxItemName.Text = item.ItemName;
            textBoxInstructions.Text = item.SpecialInstructions;
            
            List<StringValue> adjustmentList = new List<StringValue>();
            if (item.ItemType == "Drink") {
                adjustmentList.Add( new StringValue(item.Size) );
                adjustmentList.Add( new StringValue(item.Temperature) );
                adjustmentList.Add( new StringValue(item.IceLevel.Contains('%')? item.IceLevel : item.IceLevel + " I") );
                adjustmentList.Add( new StringValue(item.SugarLevel.Contains('%')? item.SugarLevel  : item.SugarLevel + " S") );
                if (item.MilkSubsitution != null) adjustmentList.Add( new StringValue(item.MilkSubsitution) );
            }else { //This is snack
                if (item.Size != "Regular") { //Handle the case where Oolong Tea eggs have size
                    adjustmentList.Add( new StringValue(item.Size) );
                }
            }

            List<StringValue> addOnList = new List<StringValue>();
            if (item.AddOnList != null) {
                foreach (string s in item.AddOnList) {
                    addOnList.Add(new StringValue(s));
                }
            }

            dataGridViewAdjustments.DataSource = adjustmentList;
            dataGridViewToppings.DataSource = addOnList;
        }

        private void Form1_Load(object sender, EventArgs e) {         
        }

        /* Only start checking and parsing emails when the form is shown */
        private void Form1_Shown(object sender, EventArgs e) {
            Program.InitApp();
        }

        /* Sort new rows by the TimeReceieved field of the Orders */
        private void dataGridView1_RowsAdded(object sender, DataGridViewRowsAddedEventArgs e) {
            //OrderContainer orderCon = new OrderContainer(new Order());
            //PropertyDescriptor pd = TypeDescriptor.GetProperties(orderCon)["TimeReceivedTicks"];
            dataGridView1.Sort(dataGridView1.Columns["TimeReceivedTicks"], ListSortDirection.Descending);
        }

        /* Changes the backcolor when the is visited. Doesn't follow the row yet and only highlights
         * the row's location, so it's not sort proof
         */
        private void dataGridView1_SelectionChanged(object sender, EventArgs e) {
            
            DataGridViewSelectedRowCollection selectedRows = dataGridView1.SelectedRows;
            if (selectedRows.Count > 0) {
                //selectedRows[0].DefaultCellStyle.BackColor = Color.BlanchedAlmond;
                UpdateOrderUI();
            }
        }

        /* Updates the item details to match the currently selected item if the row changes */
        private void dataGridView2_SelectionChanged(object sender, EventArgs e) {
            DataGridViewSelectedRowCollection selectedRows = dataGridView2.SelectedRows;
            if (selectedRows.Count > 0) {
                UpdateItemDetailsUI();
            }
        }

        /* Handles the click for each of the side bar buttons. 
         * Currently switches tabs to show a different control, but later 
         * we will show the Usercontrol corresponding to the button pressed, 
         * and set the color highlighting appropriately for each button
         */
        private void handleSideBarButtonClick(object sender, EventArgs e) {

            //Gets the current button's tag and returns if it's already the selected page
            string pressedTag = (string)((Button)sender).Tag;
       
            if (pressedTag == CurrentForm) {
                return;
            }

            List<Button> buttons = new List<Button>() {
                buttonOrdersTab,
                buttonSettingsTab,
                buttonAboutTab,
                buttonAnalyticsTab,
                buttonActionsTab,
                buttonMenusTab
            };

            //Iterate through the buttons to appropriately set the correct properties
            foreach (Button button in buttons) {

                String currentTag = (string)button.Tag;

                if (currentTag != pressedTag) {
                    button.BackColor = ColorTranslator.FromHtml("#2A2729");
                }else {
                    button.BackColor = ColorTranslator.FromHtml("#171516");
                    CurrentForm = currentTag;
                    labelTitle.Text = currentTag;
                }
            }
        }

        private void button_MouseEnter(object sender, EventArgs e) {
            Cursor = Cursors.Hand;
        }

        private void button_MouseLeave(object sender, EventArgs e) {
            Cursor = Cursors.Default;
        }
    }

    /* The object that will be data-bound to the orderGridView */
    [DataObject]
    public class OrderContainer {

        public Order order;
        public string[][] orderArray;

        /* Constructor */
        public OrderContainer(Order _order) {
            order = _order;
            orderArray = PrinterUtility.OrderToArray(order);
            Status = "Active";
            PrintStatus = "Not Printed"; //maybe use Enum for print status later?
            PrintCount = 0;
        }

        public string Status { get; set; }

        public string Service {
            get { return order.Service; }
        }

        public string Name {
            get { return order.CustomerName; }
        }

        public string ItemCount {
            get { return order.OrderSize.ToString(); }
        }

        /* Returns the time if the order is from today, else it returns the date */
        public string TimeReceived {
            get {     
                string dateNow = DateTime.Now.Date.ToString("d");
                string receivedDate = order.TimeReceived.ToString("d");
                string receivedTime = order.TimeReceived.ToString(@"hh\:mm tt");

                bool orderIsFromToday = dateNow == receivedDate;

                if (orderIsFromToday) 
                    return receivedTime;
                return receivedDate;    
            }
        }

        public string PickUpTime {
            get {
                string dateNow = DateTime.Now.Date.ToString("d");
                string pickUpDate = order.PickUpTime.ToString("d");
                string pickUpTime = order.PickUpTime.ToString(@"hh\:mm tt");

                bool orderIsFromToday = dateNow == pickUpDate;

                if (orderIsFromToday)
                    return pickUpTime;
                return pickUpDate;
            }
        }

        public long TimeReceivedTicks {
            get { return order.TimeReceived.Ticks; }
        }

        public string PrintStatus { get; set; }

        public int PrintCount { get; set; }
    }

    /* A work around for binding a List<string> to a dataGridView.
     * dataGridView looks for properties of containing objects, so we have to wrap string in a class
     */
    public class StringValue {
        public StringValue(string s) {
            Value = s;
        }

        public string Value { get; set; }
    }
}
