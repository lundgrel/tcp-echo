@echo off
echo === Installing the TCP Echo CLIENT service (TcpEchoClient) ===
rem Installs TCP Echo Client as a Windows service (run as Administrator).
rem Extra arguments are passed to the exe, e.g.: install-client-service.bat -p 6000
net session >nul 2>&1 || (echo Please run this from an elevated prompt. & exit /b 1)
sc.exe create TcpEchoClient binPath= "\"%~dp0tcp-echo-client.exe\" %*" start= auto DisplayName= "TCP Echo Client"
if errorlevel 1 exit /b %errorlevel%
sc.exe description TcpEchoClient "Keeps a TCP connection open to the echo server and exchanges periodic messages."
echo Installed. Start with: sc.exe start TcpEchoClient   (or tcp-echo-client.exe start)
