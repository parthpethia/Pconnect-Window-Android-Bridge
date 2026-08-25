import java.io.FileInputStream
import java.util.Properties

plugins {
    id("com.android.application")
    id("kotlin-android")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

val keystoreProperties = Properties()
val appKeyFile = file("key.properties")
val rootKeyFile = rootProject.file("key.properties")
val keyFileToLoad = if (appKeyFile.exists()) appKeyFile else rootKeyFile
println("--- KEY FILE TO LOAD: ${keyFileToLoad.absolutePath} (exists: ${keyFileToLoad.exists()}) ---")
if (keyFileToLoad.exists()) {
    keystoreProperties.load(FileInputStream(keyFileToLoad))
}

android {
    namespace = "com.pconnect.app"
    compileSdk = 37
    ndkVersion = flutter.ndkVersion

    compileOptions {
        isCoreLibraryDesugaringEnabled = true
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = JavaVersion.VERSION_17.toString()
    }

    defaultConfig {
        applicationId = "com.pconnect.app"
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    signingConfigs {
        create("release") {
            val alias = keystoreProperties.getProperty("keyAlias") ?: "upload"
            val pass = keystoreProperties.getProperty("keyPassword") ?: "pconnect2026upload"
            val storePass = keystoreProperties.getProperty("storePassword") ?: "pconnect2026upload"
            val sFile = keystoreProperties.getProperty("storeFile") ?: "upload-keystore.jks"

            val resolvedFile = if (file(sFile).exists()) file(sFile) else rootProject.file(sFile)
            println("--- RELEASE KEYSTORE RESOLVED PATH: ${resolvedFile.absolutePath} (exists: ${resolvedFile.exists()}) ---")
            keyAlias = alias
            keyPassword = pass
            storePassword = storePass
            storeFile = resolvedFile
        }
        getByName("debug") {
            val alias = keystoreProperties.getProperty("keyAlias") ?: "upload"
            val pass = keystoreProperties.getProperty("keyPassword") ?: "pconnect2026upload"
            val storePass = keystoreProperties.getProperty("storePassword") ?: "pconnect2026upload"
            val sFile = keystoreProperties.getProperty("storeFile") ?: "upload-keystore.jks"

            val resolvedFile = if (file(sFile).exists()) file(sFile) else rootProject.file(sFile)
            keyAlias = alias
            keyPassword = pass
            storePassword = storePass
            storeFile = resolvedFile
        }
    }

    lint {
        checkReleaseBuilds = false
        abortOnError = false
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.getByName("release")
        }
        debug {
            signingConfig = signingConfigs.getByName("release")
        }
    }
}

afterEvaluate {
    android.buildTypes.getByName("release").signingConfig = android.signingConfigs.getByName("release")
    println("--- AFTER EVALUATION RELEASE SIGNING STOREFILE: ${android.buildTypes.getByName("release").signingConfig?.storeFile} ---")
    println("--- AFTER EVALUATION RELEASE SIGNING ALIAS: ${android.buildTypes.getByName("release").signingConfig?.keyAlias} ---")
}

tasks.configureEach {
    if (name.contains("lintVital")) {
        enabled = false
    }
}

flutter {
    source = "../.."
}

dependencies {
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")
}
