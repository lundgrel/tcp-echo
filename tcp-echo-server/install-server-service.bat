@echo off
echo === Installing the TCP Echo SERVER service (TcpEchoServer) ===
rem Installs TCP Echo Server as a Windows service (run as Administrator).
rem Extra arguments are passed to the exe, e.g.: install-server-service.bat -p 6000
net session >nul 2>&1 || (echo Please run this from an elevated prompt. & exit /b 1)
sc.exe create TcpEchoServer binPath= "\"%~dp0tcp-echo-server.exe\" %*" start= auto DisplayName= "TCP Echo Server"
if errorlevel 1 exit /b %errorlevel%
sc.exe description TcpEchoServer "Accepts TCP echo clients and pushes periodic events to them."
echo Installed. Start with: sc.exe start TcpEchoServer   (or tcp-echo-server.exe start)
