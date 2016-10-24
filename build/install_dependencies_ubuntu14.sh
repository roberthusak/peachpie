# According to https://www.microsoft.com/net/core#ubuntu and http://www.ubuntuupdates.org/ppa/getdeb_games?dist=trusty

# Add the dotnet apt-get feed
sudo sh -c 'echo "deb [arch=amd64] https://apt-mo.trafficmanager.net/repos/dotnet-release/ trusty main" > /etc/apt/sources.list.d/dotnetdev.list'
sudo apt-key adv --keyserver apt-mo.trafficmanager.net --recv-keys 417A0893

# Add the GetDeb Games apt-get feed
wget -q -O - http://archive.getdeb.net/getdeb-archive.key | sudo apt-key add -
sudo sh -c 'echo "deb http://archive.getdeb.net/ubuntu trusty-getdeb games" >> /etc/apt/sources.list.d/getdeb.list'

# Update apt-get cache
sudo apt-get update

# Install .NET Core SDK
sudo apt-get install dotnet-dev-1.0.0-preview2-003131

# Install nuget (together with Mono runtime and particular libraries)
sudo apt-get install nuget

# Install Python Pip and cdiff
apt-get -y install python-pip
pip -V
pip install --upgrade cdiff