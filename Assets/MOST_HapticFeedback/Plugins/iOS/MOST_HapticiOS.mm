// By SOLO :)

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

static UISelectionFeedbackGenerator *gSelection;
static UINotificationFeedbackGenerator *gNotif;
static UIImpactFeedbackGenerator *gImpactLight;
static UIImpactFeedbackGenerator *gImpactMedium;
static UIImpactFeedbackGenerator *gImpactHeavy;
static UIImpactFeedbackGenerator *gImpactRigid; // iOS 13+
static UIImpactFeedbackGenerator *gImpactSoft;  // iOS 13+

static CFTimeInterval gLastHit = 0;
static const CFTimeInterval kMinInterval = 0.02; // 20 ms

static void EnsureInit(void) {
    if (@available(iOS 10.0, *)) {
        if (!gSelection) {
            gSelection = [UISelectionFeedbackGenerator new];
            gNotif     = [UINotificationFeedbackGenerator new];
            gImpactLight  = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
            gImpactMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
            gImpactHeavy  = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];

            if (@available(iOS 13.0, *)) {
                gImpactRigid = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid];
                gImpactSoft  = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleSoft];
            }
        }
    }
}

static BOOL RateLimited(void) {
    CFTimeInterval now = CFAbsoluteTimeGetCurrent();
    if (now - gLastHit < kMinInterval) return YES;
    gLastHit = now;
    return NO;
}

extern "C" {

    // Call once after your first scene loads.
    // This primes the Taptic engine & allocates generators off the hot path.
    void MOST_HapticPrewarm(void) {
        if (@available(iOS 10.0, *)) {
            dispatch_async(dispatch_get_main_queue(), ^{
                EnsureInit();
                [gSelection prepare];
                [gNotif prepare];
                [gImpactLight prepare];
                [gImpactMedium prepare];
                [gImpactHeavy prepare];
                if (@available(iOS 13.0, *)) {
                    [gImpactRigid prepare];
                    [gImpactSoft prepare];
                }
            });
        }
    }

    // Main entry called from Unity C# (EntryPoint="MOST_HapticFeedback")
    void MOST_HapticFeedback(int type)
    {
        if (@available(iOS 10.0, *)) {
            dispatch_async(dispatch_get_main_queue(), ^{
                EnsureInit();
                if (RateLimited()) return;

                switch (type)
                {
                    case 0: // Selection
                        [gSelection prepare];
                        [gSelection selectionChanged];
                        break;

                    case 1: // Success
                        [gNotif prepare];
                        [gNotif notificationOccurred:UINotificationFeedbackTypeSuccess];
                        break;

                    case 2: // Warning
                        [gNotif prepare];
                        [gNotif notificationOccurred:UINotificationFeedbackTypeWarning];
                        break;

                    case 3: // Failure
                        [gNotif prepare];
                        [gNotif notificationOccurred:UINotificationFeedbackTypeError];
                        break;

                    case 4: // LightImpact
                        [gImpactLight prepare];
                        [gImpactLight impactOccurred];
                        break;

                    case 5: // MediumImpact
                        [gImpactMedium prepare];
                        [gImpactMedium impactOccurred];
                        break;

                    case 6: // HeavyImpact
                        [gImpactHeavy prepare];
                        [gImpactHeavy impactOccurred];
                        break;

                    case 7: // RigidImpact (13+)
                        if (@available(iOS 13.0, *)) {
                            [gImpactRigid prepare];
                            [gImpactRigid impactOccurred];
                        } else {
                            [gImpactHeavy prepare];
                            [gImpactHeavy impactOccurred];
                        }
                        break;

                    case 8: // SoftImpact (13+)
                        if (@available(iOS 13.0, *)) {
                            [gImpactSoft prepare];
                            [gImpactSoft impactOccurred];
                        } else {
                            [gImpactLight prepare];
                            [gImpactLight impactOccurred];
                        }
                        break;
                }
            });
        }
    }
}
