# Inventory Manager

A desktop app for managing products and stock. It runs on Windows 7, 8, 8.1, 10 and 11.

## Features
- **Dashboard**: a date filter (today, yesterday, this month, last month or a custom range) for sales, profit, number of sales and units sold, each compared with the previous period. It has a sales & profit chart (hourly for one day, daily for up to two months, monthly beyond that) and a top-selling products chart; hover over a chart to see exact values. Below are the current stock figures, the transactions in the selected period, and items that need restocking.
- **Products**: add, edit and delete products with purchase rate, sale price, profit per unit, vendor, category and barcode. Search, filter, sort and export to CSV.
- **Categories**: add, rename and delete categories (**Products → Categories**).
- **Sales (POS)**: scan or search products into a cart and check out. All lines go under one invoice number (`INV-000001`). The profit on the sale is shown before checkout. At checkout you can give a **discount**, record the **payment method** (Cash / bKash / Nagad / Card), and take **partial payment** - the rest goes to the customer's due ledger.
- **Customers & dues (বাকি খাতা)**: keep customers with phone and address, sell on due, receive payments (টাকা জমা), see each customer's full ledger with a running balance, and print a **due slip**.
- **Returns (ফেরত)**: return items from any sale invoice. Stock goes back, profit reverses, and the refund first reduces the customer's due, the rest in cash. A return slip can be printed.
- **Stock adjustment (সমন্বয়)**: correct stock for damage, loss or counting mistakes with a reason - recorded in history instead of silently editing the quantity.
- **Expenses (খরচ)**: track shop expenses by category and date; the dashboard shows expenses and **net profit** (profit − expenses) for the selected period.
- **Business report**: print an A4 report for any date range - sales, discounts, cost, gross profit, expenses by category, net profit, stock purchased, top products and outstanding dues (**Dashboard → Print report**).
- **Barcode labels**: print price/barcode labels on A4 label sheets (65/40/24 per sheet) or a 38×25 mm roll (**Products → Print labels**). The barcode is Code 128.
- **Order list**: a printable restock list of everything at or below its reorder level, grouped by vendor with suggested quantities (**Products → Order list**).
- **Warranty & serials**: give a product a warranty in months; after a sale the app asks for each unit's serial number (scan or type, optional). Look any serial up later to see the invoice, sale date and days of warranty left (**Sales → History → Warranty lookup**).
- **App lock (PIN)**: set a PIN in **Settings → Security**. When locked, a lock screen covers the whole window and nothing can be seen or used until the PIN is entered. The app opens locked, locks from **Lock app** in the sidebar (or **Ctrl+L**), and locks itself after 10 idle minutes. Open dialogs are closed and phone scans are refused while locked. After 5 wrong PINs, input pauses for 30 seconds.
- **Keyboard shortcuts**: change any shortcut in **Settings → Keyboard shortcuts**.
- **Auto daily backup**: once a day, closing the app saves a backup (the last 30 are kept), optionally also to a folder you choose (USB/OneDrive).
- **Stock In**: receive goods from a vendor at a purchase cost (`SI-000001`). The cost becomes the product's purchase rate.
- **Vendors**: suppliers with contact person, phone and address, plus the number of products and the total purchased from each.
- **History** (in Sales and Stock In): filters, totals, profit per line, undo (puts the stock back), receipt reprint and CSV export.
- **Barcode scanning**: use an Android phone camera over Wi-Fi, a USB/Bluetooth barcode gun, or the keyboard.
- **Receipts**: print on 58 mm or 80 mm thermal printers, or on A4.
- **Settings**: business details, currency, default reorder level, phone scanner, receipt printer, backup and restore, reset.
- **About**: app version and developer information.

## Selling (Sales)
1. Open **Sales**.
2. Scan a barcode, or type part of a product name or code and press **Enter**. Scanning the same product again adds 1 to its quantity.
3. To change a quantity or price, click the cell and type. To remove a line, click **Remove** or press **Delete**.
4. Press **Checkout** (**F12**). Stock goes down and a receipt prints if **Print receipt** is ticked.

## Receiving goods (Stock In)
1. Open **Stock In** and choose the **Vendor** (manage the list under **Vendors**).
2. Scan the items that arrived. The unit cost is filled in from the product's purchase rate; change it if the price changed.
3. Press **Save Stock In**. Stock goes up, and the new cost becomes the product's purchase rate.

If you scan a barcode that isn't in your products yet, **Add Product** opens with the barcode filled in. Save it and the product is added to the list.

## How profit is calculated
Profit = (sale price − purchase rate) × quantity. Each sale saves the purchase rate at the time of sale, so changing prices later doesn't change past profit.

Products with no purchase rate count their full sale price as profit. Enter purchase rates in the product form, or receive stock with a cost.

## Using an Android phone as the scanner
1. Connect the phone and the PC to the **same Wi-Fi or network**.
2. On the PC, open **Sales** or **Stock In** and click **Phone Scanner**. A QR code appears.
3. Scan the QR code with the phone camera and open the link in **Chrome**.
4. Chrome warns *"Your connection is not private"*. This is expected, because the PC uses its own security certificate. Tap **Advanced**, then **Proceed**.
5. Tap **Start Scanning** and allow camera access.

After each scan the phone shows the product name, price and cart total, and beeps or vibrates. Below that is the list of scanned items: use **−** / **+** to change a quantity, **×** to remove an item, **Undo last scan** to take back the last scan, or **Clear all** to start again. Switching between **Sale** and **Stock In** on the phone opens that screen on the PC. The phone stays paired, so next time you only need to open the same page. **Reset Pairing** creates a new QR code, and phones paired with the old one stop working.

**If the phone cannot connect:**
- When Windows Firewall asks the first time, allow Inventory Manager on **Private networks**.
- Make sure the phone is on the same Wi-Fi (not mobile data or a guest network).
- If the PC has more than one network, choose the other address in the **PC address** list.
- To start the scanner automatically, use **Settings → Phone Barcode Scanner → Start automatically**. The port can also be changed there (default 8443).

## USB / Bluetooth barcode scanners
Any scanner that works like a keyboard (most of them do) works in the POS without any setup: scan, and the product is added. In the product form, scanning fills the **Barcode** field.

## Receipt printer
Install the printer in Windows first. Then in **Settings → Receipt**, choose the printer and paper size (58 mm, 80 mm or A4) and check it with **Preview** or **Test Print**.

## Google Sheet sync
The app can keep a live copy of your data in a Google Sheet that you can open from any phone or computer. It is one-way: you make changes in the app, and the Sheet follows.
Tabs: products, sales (with profit), stock-ins, daily summary, vendors, categories, customers (with dues), payments, expenses and serials.

1. Create a new Google Sheet.
2. In **Settings → Google Sheet sync**, click **Copy script**. In the Sheet, open **Extensions → Apps Script**, delete what is there, paste, and press **Save** (Ctrl+S).
3. **Give permission once:** in the function list at the top of the editor choose **authorize** and click **Run**. Click **Review permissions**, pick your Google account, then **Advanced → Go to (unsafe) → Allow**. Google shows this warning for every personal script. Without this step the deployment can fail to authorize.
4. Click **Deploy → New deployment → Web app**. Set **Execute as** to **Me** and **Who has access** to **Anyone**, then click **Deploy**.
5. Copy the Web app link (it ends with `/exec`), paste it into the app, and click **Test connection**.

If you change the script later, use **Deploy → Manage deployments → Edit → Version: New version → Deploy**, so the link stays the same.

After that, every sale, stock-in, undo and product change reaches the Sheet a few seconds later. If the internet is down, the app keeps working and catches up automatically when the connection returns. The status bar shows the last sync time.
The link contains a secret key, so only this app can write to your Sheet. Don't edit the synced tabs by hand: **Re-send everything** rebuilds them.

## Language (বাংলা / English)
The app opens in **Bangla** by default: menus, messages, receipts and the phone scanner page are all in Bangla, and amounts and dates use Bangla digits and month names (৳ ১,২৫০.০০, ১৪ অক্টোবর ২০২৬).
Product codes, barcodes and invoice numbers stay in English digits so barcode scanners keep matching.
To change the language, go to **Settings → Language / ভাষা**, press **Save Settings**, and let the app restart.

The app carries its own Bangla fonts inside the exe (**IM Siliguri** for text and **IM Numbers** for figures, both based on Hind Siliguri, SIL Open Font License), so text looks the same on Windows 7, 8, 10 and 11.

## Installing
There are two ways. Neither needs an internet connection.

| | Use it on | What it is |
|---|---|---|
| `InventoryManager-Setup.exe` (≈120 MB) | **Any PC, including Windows 7** | A normal installer: adds the app to Program Files, a desktop and Start menu shortcut, and an entry in *Programs and Features* for uninstalling. It contains the offline **.NET Framework 4.8** installer and installs it only if the PC doesn't have .NET 4.5 or later (usually only Windows 7). |
| `InventoryManager\InventoryManager.exe` (≈9 MB) | Windows 8, 8.1, 10, 11 | The whole app in one file, with no installing. Copy it anywhere (even a USB drive) and double-click. These Windows versions already include the .NET it needs. |

The setup asks for administrator permission (click **Yes**). On Windows 7 the .NET step takes 5–10 minutes, and Windows may ask for one restart afterwards.

Windows 7 needs **Service Pack 1** for .NET 4.8. If the setup reports that .NET could not be installed, install SP1 from Windows Update and run the setup again.

On first start the single-file exe unpacks its helper files (SQLite engine, fonts) once to `%LocalAppData%\InventoryManager\runtime`. This is automatic and takes about a second.

## Requirements
- Windows 7 SP1 or later (32-bit or 64-bit)
- .NET Framework 4.5 or later. Windows 8, 10 and 11 already include it; the setup installs it on Windows 7.

Data is stored in `%AppData%\InventoryManager\inventory.db` (uninstalling or replacing the exe keeps it). Use **Settings → Back Up Now** to save a copy. Before every restore and reset, an automatic safety copy is saved in `%AppData%\InventoryManager\Backups`.

## Keyboard shortcuts
These are the defaults. Change them in **Settings → Keyboard shortcuts**: click a box and press the new keys. Backspace removes a shortcut and **Reset to defaults** brings these back. A shortcut must be a function key or use Ctrl/Alt. If you pick keys another action already uses, that action loses them, and the app tells you so.

| Key | Action |
|---|---|
| Ctrl+1 … Ctrl+5 | Open Dashboard, Products, Sales, Stock In, Customers |
| Ctrl+N | Add new (product, customer, expense or vendor, on that screen) |
| Ctrl+F | Go to the search box |
| F5 | Refresh the screen |
| Ctrl+L | Lock the app |
| F2 | POS: go to the scan box |
| F9 | POS: go to the discount box |
| F12 | POS: checkout / save stock-in |

Always available (not changeable): **Del** removes the selected line or item, **Enter** or double-click opens it, **Tab** moves between boxes.

## Building from source
You need Visual Studio 2019 or later, or the VS Build Tools with the .NET desktop build workload. Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The output goes to `dist`: `dist\InventoryManager\InventoryManager.exe` (single file) and `dist\InventoryManager-Setup.exe`. The first build downloads the offline .NET 4.8 installer (121 MB) once into `installer\redist`. Use `-NoSetup` to build only the exe.

---
Developed By: **Tawhidul Islam**, Senior Backend Engineer · [tawhid.playvai.com](https://tawhid.playvai.com) · smtir.dev@gmail.com
