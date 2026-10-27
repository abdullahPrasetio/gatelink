import LocalAuthentication
import Foundation

// Precompiled LocalAuthentication helper. The reason is argv[1]; it is never
// interpolated into source, so an imported otpauth issuer cannot inject Swift.
// deviceOwnerAuthenticationWithBiometrics gives a Touch ID prompt (no password
// fallback); the app falls back to the Keychain gate if it cannot evaluate.

let reason = CommandLine.arguments.count > 1
    ? CommandLine.arguments[1]
    : "Unlock GateLink"

let context = LAContext()
var error: NSError?
guard context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) else {
    exit(2)
}

let semaphore = DispatchSemaphore(value: 0)
var success = false
context.evaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, localizedReason: reason) { ok, _ in
    success = ok
    semaphore.signal()
}
semaphore.wait()
exit(success ? 0 : 1)
