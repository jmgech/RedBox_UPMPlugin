#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>
#import <dlfcn.h>
#include <string.h>

namespace {
static NSString* const kServiceUuid = @"6dd19343-28f2-4b95-b3df-57fef28599c2";
static NSString* const kTxUuid      = @"2f5bbef8-7fc7-4527-a753-fec2f7f1132b";

using UnitySendMessageFn = void(*)(const char* obj, const char* method, const char* msg);
using RKRedboxBleCallback = void(*)(const char* message);

static RKRedboxBleCallback g_onConnectedCallback = nullptr;
static RKRedboxBleCallback g_onDataCallback = nullptr;
static RKRedboxBleCallback g_onErrorCallback = nullptr;

static UnitySendMessageFn ResolveUnitySendMessage() {
    static UnitySendMessageFn fn = nullptr;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        fn = reinterpret_cast<UnitySendMessageFn>(dlsym(RTLD_DEFAULT, "UnitySendMessage"));
        if (fn == nullptr) {
            // Fallback for environments where RTLD_DEFAULT lookup is restricted.
            void* selfHandle = dlopen(nullptr, RTLD_NOW);
            if (selfHandle != nullptr) {
                fn = reinterpret_cast<UnitySendMessageFn>(dlsym(selfHandle, "UnitySendMessage"));
            }
        }

        if (fn == nullptr) {
            NSLog(@"[RKRedboxBleBridge] UnitySendMessage symbol not found; callbacks to C# are disabled.");
        }
    });

    return fn;
}
}

@interface RKRedboxBleBridge : NSObject<CBCentralManagerDelegate, CBPeripheralDelegate>
@property(nonatomic, strong) CBCentralManager* central;
@property(nonatomic, strong) CBPeripheral* peripheral;
@property(nonatomic, copy) NSString* endpoint;
@property(nonatomic, copy) NSString* unityObject;
@property(nonatomic, assign) BOOL scanning;
@property(nonatomic, assign) BOOL autoReconnect;
@end

@implementation RKRedboxBleBridge

- (instancetype)init {
    self = [super init];
    if (self) {
        _central = [[CBCentralManager alloc] initWithDelegate:self queue:dispatch_get_main_queue()];
        _scanning = NO;
        _autoReconnect = YES;
    }
    return self;
}

- (void)setUnityObjectName:(NSString*)name {
    self.unityObject = name;
}

- (BOOL)startWithEndpoint:(NSString*)endpoint {
    if (endpoint.length == 0) return NO;
    self.autoReconnect = YES;
    self.endpoint = endpoint;
    if (self.central.state == CBManagerStatePoweredOn) {
        [self startScanning];
    }
    return YES;
}

- (void)disconnect {
    self.autoReconnect = NO;
    if (self.peripheral != nil) {
        [self.central cancelPeripheralConnection:self.peripheral];
        self.peripheral = nil;
    }
    [self stopScanning];
}

- (void)startScanning {
    if (self.scanning) return;

    CBUUID* service = [CBUUID UUIDWithString:kServiceUuid];
    NSDictionary* options = @{ CBCentralManagerScanOptionAllowDuplicatesKey: @NO };
    [self.central scanForPeripheralsWithServices:@[service] options:options];
    self.scanning = YES;
}

- (void)stopScanning {
    if (!self.scanning) return;
    [self.central stopScan];
    self.scanning = NO;
}

- (void)sendUnity:(const char*)method message:(NSString*)message {
    const char* utf8Message = (message ?: @"").UTF8String;

    // Preferred path: explicit callbacks registered by managed code.
    if (method != nullptr) {
        if (strcmp(method, "OnBleConnected") == 0 && g_onConnectedCallback != nullptr) {
            g_onConnectedCallback(utf8Message);
            return;
        }
        if (strcmp(method, "OnBleData") == 0 && g_onDataCallback != nullptr) {
            g_onDataCallback(utf8Message);
            return;
        }
        if (strcmp(method, "OnBleError") == 0 && g_onErrorCallback != nullptr) {
            g_onErrorCallback(utf8Message);
            return;
        }
    }

    if (self.unityObject.length == 0) return;
    UnitySendMessageFn unitySendMessage = ResolveUnitySendMessage();
    if (unitySendMessage == nullptr) return;

    unitySendMessage(self.unityObject.UTF8String, method, utf8Message);
}

- (void)sendConnected:(BOOL)connected {
    [self sendUnity:"OnBleConnected" message:(connected ? @"1" : @"0")];
}

- (void)sendError:(NSString*)error {
    [self sendUnity:"OnBleError" message:error ?: @"Unknown BLE error"];
}

- (BOOL)matchesEndpoint:(CBPeripheral*)p advertisementData:(NSDictionary<NSString*,id>*)advertisementData {
    NSString* name = p.name;
    NSString* localName = advertisementData[CBAdvertisementDataLocalNameKey];
    if (name.length > 0 && [name localizedCaseInsensitiveContainsString:self.endpoint]) return YES;
    if (localName.length > 0 && [localName localizedCaseInsensitiveContainsString:self.endpoint]) return YES;
    return NO;
}

- (void)centralManagerDidUpdateState:(CBCentralManager *)central {
    if (central.state == CBManagerStatePoweredOn) {
        if (self.endpoint.length > 0) [self startScanning];
    } else {
        [self sendConnected:NO];
    }
}

- (void)centralManager:(CBCentralManager *)central
 didDiscoverPeripheral:(CBPeripheral *)peripheral
     advertisementData:(NSDictionary<NSString *,id> *)advertisementData
                  RSSI:(NSNumber *)RSSI {
    (void)central; (void)RSSI;
    if (![self matchesEndpoint:peripheral advertisementData:advertisementData]) return;

    [self stopScanning];
    self.peripheral = peripheral;
    self.peripheral.delegate = self;
    [self.central connectPeripheral:peripheral options:nil];
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral {
    (void)central;
    [self sendConnected:YES];

    CBUUID* service = [CBUUID UUIDWithString:kServiceUuid];
    [peripheral discoverServices:@[service]];
}

- (void)centralManager:(CBCentralManager *)central didDisconnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    (void)central; (void)peripheral;
    [self sendConnected:NO];
    if (error != nil) [self sendError:error.localizedDescription];
    if (self.autoReconnect) [self startScanning];
}

- (void)centralManager:(CBCentralManager *)central didFailToConnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    (void)central; (void)peripheral;
    [self sendConnected:NO];
    [self sendError:error.localizedDescription];
    if (self.autoReconnect) [self startScanning];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error {
    if (error != nil) { [self sendError:error.localizedDescription]; return; }

    CBUUID* txUuid = [CBUUID UUIDWithString:kTxUuid];
    for (CBService* service in peripheral.services) {
        [peripheral discoverCharacteristics:@[txUuid] forService:service];
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error {
    if (error != nil) { [self sendError:error.localizedDescription]; return; }

    CBUUID* txUuid = [CBUUID UUIDWithString:kTxUuid];
    for (CBCharacteristic* characteristic in service.characteristics) {
        if ([characteristic.UUID isEqual:txUuid]) {
            [peripheral setNotifyValue:YES forCharacteristic:characteristic];
        }
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    (void)peripheral;
    if (error != nil) { [self sendError:error.localizedDescription]; return; }

    NSData* data = characteristic.value;
    if (data.length == 0) return;

    NSString* base64 = [data base64EncodedStringWithOptions:0];
    [self sendUnity:"OnBleData" message:base64];
}

@end

static RKRedboxBleBridge* g_bridge = nil;

extern "C" {

void RKRedboxBle_SetCallbacks(RKRedboxBleCallback onConnected,
                              RKRedboxBleCallback onData,
                              RKRedboxBleCallback onError) {
    g_onConnectedCallback = onConnected;
    g_onDataCallback = onData;
    g_onErrorCallback = onError;
}

void RKRedboxBle_Initialize(const char* gameObjectName) {
    @autoreleasepool {
        // Always recreate bridge between play sessions to avoid stale
        // CoreBluetooth state in the Unity Editor process.
        if (g_bridge != nil) {
            [g_bridge disconnect];
            g_bridge = nil;
        }
        g_bridge = [[RKRedboxBleBridge alloc] init];

        NSString* objectName = gameObjectName != nullptr
            ? [NSString stringWithUTF8String:gameObjectName]
            : @"";
        [g_bridge setUnityObjectName:objectName];
    }
}

bool RKRedboxBle_StartScanAndConnect(const char* endpoint) {
    @autoreleasepool {
        if (g_bridge == nil || endpoint == nullptr) return false;
        NSString* endpointString = [NSString stringWithUTF8String:endpoint];
        return [g_bridge startWithEndpoint:endpointString];
    }
}

void RKRedboxBle_Disconnect(void) {
    @autoreleasepool {
        if (g_bridge != nil) {
            [g_bridge disconnect];
        }
    }
}

}
