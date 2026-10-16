#import <AppKit/AppKit.h>
#import <PDFKit/PDFKit.h>
#import <ApplicationServices/ApplicationServices.h>
#include <string.h>

static NSPrintInfo *TheOnePaperInfo(const char *printer, double widthMm, double heightMm, int landscape)
{
    if (![NSThread isMainThread]) @throw [NSException exceptionWithName:@"Printing" reason:@"macOS 打印必须在主线程调用。" userInfo:nil];
    NSString *queue = [NSString stringWithUTF8String:printer];
    CFArrayRef printers = NULL;
    if (PMServerCreatePrinterList(kPMServerLocal, &printers) != noErr || !printers)
        @throw [NSException exceptionWithName:@"Printing" reason:@"无法读取 macOS 打印机。" userInfo:nil];
    @try {
        for (CFIndex i = 0; i < CFArrayGetCount(printers); i++) {
            PMPrinter device = (PMPrinter)CFArrayGetValueAtIndex(printers, i);
            if (![(__bridge NSString *)PMPrinterGetID(device) isEqualToString:queue]) continue;
            // AppKit uses the display name; CUPS uses PrintCore's stable queue ID.
            NSPrinter *selected = [NSPrinter printerWithName:(__bridge NSString *)PMPrinterGetName(device)];
            if (!selected) break;
            NSPrintInfo *info = [[NSPrintInfo sharedPrintInfo] copy];
            info.printer = selected;
            if (PMSessionSetCurrentPMPrinter((PMPrintSession)[info PMPrintSession], device) != noErr) break;
            info.jobDisposition = NSPrintSpoolJob;
            info.paperSize = NSMakeSize(widthMm * 72.0 / 25.4, heightMm * 72.0 / 25.4);
            info.orientation = landscape ? NSPaperOrientationLandscape : NSPaperOrientationPortrait;
            info.leftMargin = info.rightMargin = info.topMargin = info.bottomMargin = 0;
            info.horizontallyCentered = NO; info.verticallyCentered = NO;
            info.dictionary[NSPrintCopies] = @1;
            info.dictionary[NSPrintAllPages] = @YES;
            info.dictionary[NSPrintScalingFactor] = @1.0;
            return info;
        }
    } @finally { CFRelease(printers); }
    @throw [NSException exceptionWithName:@"Printing" reason:@"该队列不在 macOS 的可用设备列表中，请选择其他打印机。" userInfo:nil];
}

__attribute__((visibility("default")))
int TheOneGetImageableArea(const char *printer, double widthMm, double heightMm, int landscape,
                          double *left, double *top, double *right, double *bottom, char *error, int errorSize)
{
    @autoreleasepool {
        @try {
            NSPrintInfo *info = TheOnePaperInfo(printer, widthMm, heightMm, landscape);
            NSRect bounds = info.imageablePageBounds; NSSize size = info.paperSize;
            if (bounds.size.width <= 0 || bounds.size.height <= 0)
                @throw [NSException exceptionWithName:@"Printing" reason:@"macOS 未报告有效可打印区域。" userInfo:nil];
            *left = MAX(0, NSMinX(bounds)) * 25.4 / 72.0;
            *top = MAX(0, size.height - NSMaxY(bounds)) * 25.4 / 72.0;
            *right = MAX(0, size.width - NSMaxX(bounds)) * 25.4 / 72.0;
            *bottom = MAX(0, NSMinY(bounds)) * 25.4 / 72.0;
            return 1;
        } @catch (NSException *exception) {
            if (error && errorSize > 0) strlcpy(error, exception.reason.UTF8String ?: "macOS page setup failed", (size_t)errorSize);
            return 0;
        }
    }
}

// Original minimal bridge. No dependency on upstream PrintingTools binaries or consumer-side compilation.
__attribute__((visibility("default")))
int TheOnePrintPdf(const char *path, const char *printer, const char *title,
                  double widthMm, double heightMm, int landscape, int duplex, int color, char *error, int errorSize)
{
    @autoreleasepool {
        @try {
            NSPrintInfo *info = TheOnePaperInfo(printer, widthMm, heightMm, landscape);
            PDFDocument *document = [[PDFDocument alloc] initWithURL:[NSURL fileURLWithPath:[NSString stringWithUTF8String:path]]];
            if (!document || document.pageCount == 0) @throw [NSException exceptionWithName:@"Printing" reason:@"无法读取打印 PDF。" userInfo:nil];
            info.dictionary[NSPrintFirstPage] = @1;
            info.dictionary[NSPrintLastPage] = @(document.pageCount);
            PMPrintSettings nativeSettings = (PMPrintSettings)[info PMPrintSettings];
            PMSetCopies(nativeSettings, 1, false);
            PMPrintSettingsSetValue(nativeSettings, CFSTR("print-color-mode"), color ? CFSTR("color") : CFSTR("monochrome"), false);
            PMPrintSettingsSetValue(nativeSettings, CFSTR("ColorModel"), color ? CFSTR("RGB") : CFSTR("Gray"), false);
            OSStatus status = PMSetDuplex(nativeSettings, duplex == 1 ? kPMDuplexNoTumble : duplex == 2 ? kPMDuplexTumble : kPMDuplexNone);
            if (status != noErr) @throw [NSException exceptionWithName:@"Printing" reason:@"打印机拒绝双面设置。" userInfo:nil];
            NSPrintOperation *operation = [document printOperationForPrintInfo:info scalingMode:kPDFPrintPageScaleNone autoRotate:NO];
            if (!operation) @throw [NSException exceptionWithName:@"Printing" reason:@"无法创建 macOS 打印任务。" userInfo:nil];
            operation.jobTitle = [NSString stringWithUTF8String:title];
            operation.showsPrintPanel = NO; operation.showsProgressPanel = NO;
            operation.canSpawnSeparateThread = YES;
            if (![operation runOperation]) @throw [NSException exceptionWithName:@"Printing" reason:@"macOS 打印操作未正常完成或已取消，可能已输出部分页面；请先检查系统队列。" userInfo:nil];
            return 1;
        } @catch (NSException *exception) {
            if (error && errorSize > 0) strlcpy(error, exception.reason.UTF8String ?: "macOS printing failed", (size_t)errorSize);
            return 0;
        }
    }
}
