namespace FsOperator 

module StartPrompts =

    let amazon = 
        "https://amazon.com",
        """On Amazon, find me an iphone 16 pro max case that has 
**built in screen protector** and is less than $50 with good rating.
*make sure the price is less than $50*
Use the search box to find products. 
**Ignore any sign-in pages and continue without signing in**
I just want to search for products not purchase them yet.
"""


    let linkedIn = 
        "https://www.linkedin.com",
        """
Summarize what my connections have posted today
on LinkedIn.
"""


    let twitter = 
        "https://twitter.com",
        """
On twitter find out if anyone has posted about 
generative AI in the recent past and 
summarize the postings
"""

    let netflix = 
        "https://www.netflix.com",
        """
On netflix.com search for well rate scifi movies 
and give me a list
"""
